using System.Collections.Concurrent;

namespace Delibera.Core.Memory;

/// <summary>
///    A single memory entry — a fact, conclusion, or context snippet that a council
///    member wants to remember across separate debate sessions.
/// </summary>
/// <param name="Content">The text content of the memory.</param>
/// <param name="CreatedAt">UTC timestamp when the memory was stored.</param>
/// <param name="Metadata">
///    Optional metadata keyed by tag name (e.g. <c>{"source": "debate-id-123"}</c>).
///    Used for filtering during recall and for downstream analytics.
/// </param>
public sealed record MemoryEntry(
   string Content,
   DateTimeOffset CreatedAt,
   IReadOnlyDictionary<string, string> Metadata);

/// <summary>
///    Persistent memory for individual council members across separate debate
///    sessions. Memories are stored per agent (identified by <c>agentName</c>) and
///    recalled by semantic similarity to a query.
/// </summary>
/// <remarks>
///    <para>
///       Use <see cref="ICouncilBuilder.WithAgentMemory(IAgentMemory?)" /> to attach a
///       memory backend. The <see cref="Council.CouncilExecutor" /> automatically:
///    </para>
///    <list type="bullet">
///       <item>
///          Stores each member's final response (and any final-verdict text) as
///          a memory after the debate completes, tagged with the debate id and member
///          name.
///       </item>
///       <item>
///          Injects the top-K recalled memories into each member's system prompt
///          (clearly marked as <c>[Memory from previous sessions]</c>) at the start of
///          the next debate the member participates in.
///       </item>
///    </list>
///    <para>
///       Implementations must be thread-safe — the same memory instance may be
///       shared across multiple council executors running in parallel.
///    </para>
/// </remarks>
public interface IAgentMemory
{
   /// <summary>
   ///    Stores a memory entry for the given agent.
   /// </summary>
   /// <param name="agentName">Agent identifier (typically the member's display name).</param>
   /// <param name="entry">The memory entry to store.</param>
   /// <param name="ct">Cancellation token.</param>
   Task StoreAsync(string agentName, MemoryEntry entry, CancellationToken ct = default);

   /// <summary>
   ///    Recalls the most relevant memories for the given agent, ranked by similarity
   ///    to <paramref name="query" />. Returns at most <paramref name="limit" /> entries.
   /// </summary>
   /// <param name="agentName">Agent identifier.</param>
   /// <param name="query">Query text used for semantic similarity ranking.</param>
   /// <param name="limit">Maximum number of entries to return (default 5).</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Read-only list of recalled memories, most-relevant first.</returns>
   Task<IReadOnlyList<MemoryEntry>> RecallAsync(
      string agentName, string query, int limit = 5, CancellationToken ct = default);

   /// <summary>
   ///    Deletes all memories for the given agent. Useful for tests or GDPR-style
   ///    right-to-be-forgotten flows.
   /// </summary>
   /// <param name="agentName">Agent identifier.</param>
   /// <param name="ct">Cancellation token.</param>
   Task DeleteAsync(string agentName, CancellationToken ct = default);
}

/// <summary>
///    In-memory implementation of <see cref="IAgentMemory" />. Default when no
///    persistent backend is configured. Memories are lost on process exit.
/// </summary>
/// <remarks>
///    Uses simple keyword overlap (Jaccard similarity) as the relevance metric.
///    Production deployments should use <see cref="QdrantAgentMemory" /> or
///    <see cref="PgVectorAgentMemory" /> for semantic embeddings.
/// </remarks>
public sealed class InMemoryAgentMemory : IAgentMemory
{
   private readonly ConcurrentDictionary<string, List<MemoryEntry>> _memories = new();

   /// <inheritdoc />
   public Task StoreAsync(string agentName, MemoryEntry entry, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      ArgumentNullException.ThrowIfNull(entry);
      ct.ThrowIfCancellationRequested();
      _memories.AddOrUpdate(
         agentName,
         _ => [entry],
         (_, list) =>
         {
            lock (list)
            {
               list.Add(entry);
               return list;
            }
         });
      return Task.CompletedTask;
   }

   /// <inheritdoc />
   public Task<IReadOnlyList<MemoryEntry>> RecallAsync(
      string agentName, string query, int limit = 5, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      ct.ThrowIfCancellationRequested();
      if (!_memories.TryGetValue(agentName, out var list) || list.Count == 0)
         return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

      var queryTokens = Tokenize(query);
      IReadOnlyList<MemoryEntry> snapshot;
      lock (list)
      {
         snapshot = list.ToList();
      }

      var scored = snapshot
         .Select(m => (memory: m, score: JaccardSimilarity(queryTokens, Tokenize(m.Content))))
         .Where(t => t.score > 0)
         .OrderByDescending(t => t.score)
         .Take(Math.Max(1, limit))
         .Select(t => t.memory)
         .ToList();
      return Task.FromResult<IReadOnlyList<MemoryEntry>>(scored);
   }

   /// <inheritdoc />
   public Task DeleteAsync(string agentName, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      _memories.TryRemove(agentName, out _);
      return Task.CompletedTask;
   }

   private static HashSet<string> Tokenize(string text)
   {
      if (string.IsNullOrWhiteSpace(text)) return [];
      return new HashSet<string>(
         text.Split([' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\''],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
         StringComparer.OrdinalIgnoreCase);
   }

   private static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
   {
      if (a.Count == 0 || b.Count == 0) return 0;
      var intersection = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
      var union = a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
      return union == 0
         ? 0
         : (double)intersection / union;
   }
}

/// <summary>
///    Qdrant-backed implementation of <see cref="IAgentMemory" />. Uses an existing
///    <see cref="Interfaces.IRagProvider" /> for storage and an
///    <see cref="Interfaces.IEmbeddingProvider" /> for semantic similarity. Each
///    agent gets its own Qdrant collection (named <c>agent_memory_{agentName}</c>) so
///    memories are isolated per agent.
/// </summary>
public sealed class QdrantAgentMemory : IAgentMemory
{
   private readonly string _collectionPrefix;
   private readonly IEmbeddingProvider _embeddingProvider;
   private readonly IRagProvider _ragProvider;

   /// <summary>
   ///    Creates a Qdrant-backed memory store. Each agent's memories live in a
   ///    collection named <c>{collectionPrefix}{agentName}</c>.
   /// </summary>
   /// <param name="ragProvider">An existing RAG provider (Qdrant-backed).</param>
   /// <param name="embeddingProvider">An existing embedding provider for semantic similarity.</param>
   /// <param name="collectionPrefix">Collection name prefix. Default is <c>"agent_memory_"</c>.</param>
   public QdrantAgentMemory(
      IRagProvider ragProvider,
      IEmbeddingProvider embeddingProvider,
      string collectionPrefix = "agent_memory_")
   {
      ArgumentNullException.ThrowIfNull(ragProvider);
      ArgumentNullException.ThrowIfNull(embeddingProvider);
      _ragProvider = ragProvider;
      _embeddingProvider = embeddingProvider;
      _collectionPrefix = collectionPrefix;
   }

   /// <inheritdoc />
   public async Task StoreAsync(string agentName, MemoryEntry entry, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      ArgumentNullException.ThrowIfNull(entry);
      var collection = _collectionPrefix + SanitizeName(agentName);
      var meta = new Dictionary<string, string>(entry.Metadata);
      await _ragProvider.IndexDocumentAsync(collection, entry.Content, meta, 1000, 0, ct).ConfigureAwait(false);
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(
      string agentName, string query, int limit = 5, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      var collection = _collectionPrefix + SanitizeName(agentName);
      var results = await _ragProvider.SearchAsync(collection, query, limit, 0.0f, ct).ConfigureAwait(false);
      return results
         .Select(r => new MemoryEntry(r.Text, DateTimeOffset.UtcNow, r.Metadata))
         .ToList();
   }

   /// <inheritdoc />
   public Task DeleteAsync(string agentName, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      // RAG providers typically don't expose collection deletion; the Qdrant
      // provider's IndexDocumentAsync with overwrite semantics effectively
      // replaces content. Full GDPR-style purge requires direct Qdrant client
      // access — out of scope for this v1.
      return Task.CompletedTask;
   }

   private static string SanitizeName(string name)
   {
      var sb = new StringBuilder(name.Length);
      foreach (var c in name)
         sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-'
            ? c
            : '_');
      return sb.ToString();
   }
}

/// <summary>
///    PostgreSQL pgvector-backed implementation of <see cref="IAgentMemory" />. Uses
///    an existing <see cref="Interfaces.IRagProvider" /> for storage and an
///    <see cref="Interfaces.IEmbeddingProvider" /> for semantic similarity. Each
///    agent's memories share a single pgvector table but are filtered by an
///    <c>agent_name</c> column for isolation.
/// </summary>
public sealed class PgVectorAgentMemory : IAgentMemory
{
   private readonly IEmbeddingProvider _embeddingProvider;
   private readonly IRagProvider _ragProvider;
   private readonly string _tableName;

   /// <summary>
   ///    Creates a pgvector-backed memory store. All agents share the table
   ///    <paramref name="tableName" />, with rows tagged by <c>agent_name</c>.
   /// </summary>
   /// <param name="ragProvider">An existing RAG provider (pgvector-backed).</param>
   /// <param name="embeddingProvider">An existing embedding provider.</param>
   /// <param name="tableName">Table name. Default is <c>"agent_memory"</c>.</param>
   public PgVectorAgentMemory(
      IRagProvider ragProvider,
      IEmbeddingProvider embeddingProvider,
      string tableName = "agent_memory")
   {
      ArgumentNullException.ThrowIfNull(ragProvider);
      ArgumentNullException.ThrowIfNull(embeddingProvider);
      _ragProvider = ragProvider;
      _embeddingProvider = embeddingProvider;
      _tableName = tableName;
   }

   /// <inheritdoc />
   public async Task StoreAsync(string agentName, MemoryEntry entry, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      ArgumentNullException.ThrowIfNull(entry);
      var meta = new Dictionary<string, string>(entry.Metadata) { ["agent_name"] = agentName };
      await _ragProvider.IndexDocumentAsync(_tableName, entry.Content, meta, 1000, 0, ct).ConfigureAwait(false);
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(
      string agentName, string query, int limit = 5, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      // Note: precise pgvector-level filtering by agent_name is delegated to
      // the IRagProvider implementation. The PgVector provider should expose
      // a metadata-filtered query; if not, the caller can post-filter the results.
      var results = await _ragProvider.SearchAsync(_tableName, query, limit * 2, 0.0f, ct).ConfigureAwait(false);
      return results
         .Where(r => r.Metadata.TryGetValue("agent_name", out var name) && name == agentName)
         .Take(Math.Max(1, limit))
         .Select(r => new MemoryEntry(r.Text, DateTimeOffset.UtcNow, r.Metadata))
         .ToList();
   }

   /// <inheritdoc />
   public Task DeleteAsync(string agentName, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
      // Same caveat as QdrantAgentMemory — full purge requires direct pgvector
      // client access. The RAG provider's metadata-filtered delete can be wired
      // here when available.
      return Task.CompletedTask;
   }
}
