using System.Security.Cryptography;

namespace Delibera.Core.Providers.RAG;

/// <summary>
///    Shared base for vector-store backed RAG providers. Holds the embedding provider,
///    the vector store, the chunking logic and the convenience text-formatter used by
///    <see cref="GetContextAsync" />. Subclasses provide only the constructor that
///    wires up the specific <see cref="IVectorStore" />.
/// </summary>
public abstract class BaseRagProvider : IRagProvider
{
   /// <summary>
   ///    Initialises a base RAG provider with the supplied vector store and embedding provider.
   /// </summary>
   /// <param name="vectorStore">Vector store used for persistence and search.</param>
   /// <param name="embeddingProvider">Embedding provider used to vectorise chunks.</param>
   /// <param name="logger">
   ///    Optional logger. When supplied, <see cref="IndexFileAsync" /> reports a collection
   ///    that still holds legacy random-id points left by Delibera 10.5.0 and earlier.
   /// </param>
   protected BaseRagProvider(
      IVectorStore vectorStore,
      IEmbeddingProvider embeddingProvider,
      ILogger? logger = null)
   {
      VectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
      EmbeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
      Logger = logger;
   }

   /// <summary>Logger used to surface indexing anomalies. <c>null</c> disables that reporting.</summary>
   protected ILogger? Logger { get; }

   /// <summary>Abstract <see cref="IRagProvider" /> implementations describe themselves.</summary>
   public abstract string ProviderName { get; }

   /// <inheritdoc />
   public IVectorStore VectorStore { get; }

   /// <inheritdoc />
   public IEmbeddingProvider EmbeddingProvider { get; }

   /// <inheritdoc />
   public virtual async Task<int> IndexDocumentAsync(
      string collectionName,
      string documentText,
      Dictionary<string, string>? metadata = null,
      int chunkSize = 500,
      int chunkOverlap = 50,
      CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
      ArgumentException.ThrowIfNullOrWhiteSpace(documentText);

      var chunks = TextChunker.SplitIntoChunks(documentText, chunkSize, chunkOverlap);
      if (chunks.Count == 0) return 0;

      // Compute embeddings in batch
      var vectors = await EmbeddingProvider.EmbedBatchAsync(chunks, ct).ConfigureAwait(false);

      // Ensure collection/table exists
      await VectorStore.EnsureCollectionAsync(collectionName, vectors[0].Length, ct).ConfigureAwait(false);

      // Build points. The id is derived from the chunk's identity rather than a fresh GUID:
      // both concrete stores treat the id as an upsert key (Qdrant point id, pgvector
      // "ON CONFLICT (id) DO UPDATE"), so a stable id turns re-indexing into a replace
      // instead of an append. A random id per call meant every run duplicated the corpus.
      var points = new List<VectorPoint>(chunks.Count);
      for (var i = 0; i < chunks.Count; i++)
      {
         var pointMeta = metadata is not null
            ? new Dictionary<string, string>(metadata)
            : new Dictionary<string, string>();
         pointMeta["chunk_index"] = i.ToString();

         points.Add(new VectorPoint(
            ComputePointId(collectionName, metadata, i, chunks[i]),
            vectors[i],
            chunks[i],
            pointMeta));
      }

      await VectorStore.UpsertAsync(collectionName, points, ct).ConfigureAwait(false);
      return chunks.Count;
   }

   /// <inheritdoc />
   public virtual async Task<int> IndexFileAsync(
      string collectionName,
      string filePath,
      int chunkSize = 500,
      int chunkOverlap = 50,
      CancellationToken ct = default)
   {
      var fullPath = Path.GetFullPath(filePath);
      if (!File.Exists(fullPath))
         throw new FileNotFoundException($"File not found: {fullPath}");

      var text = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
      var meta = new Dictionary<string, string>
      {
         ["source"] = Path.GetFileName(fullPath),
         ["source_path"] = fullPath
      };

      // Read the pre-existing point count before indexing so a collection written by Delibera
      // 10.5.0 or earlier (random point ids) can be called out. Those points are never replaced,
      // so they survive alongside the new deterministic ones and keep skewing search results.
      var existingCount = await TryCountAsync(collectionName, ct).ConfigureAwait(false);

      var indexed = await IndexDocumentAsync(collectionName, text, meta, chunkSize, chunkOverlap, ct).ConfigureAwait(false);

      if (Logger is not null && existingCount > indexed)
      {
         Logger.LogWarning(
            "Collection '{Collection}' held {ExistingCount} points before indexing '{File}', but this document contributes only {IndexedCount}. Delibera 10.5.0 and earlier assigned a random point id per chunk, so those points are never replaced by re-indexing and keep diluting search results. Delete the collection through IVectorStore.DeleteCollectionAsync and re-index once to remove them.",
            collectionName,
            existingCount,
            fullPath,
            indexed);
      }

      return indexed;
   }

   /// <summary>
   ///    Reads the current point count for a collection, returning -1 when the collection does not
   ///    exist yet. Both concrete stores throw on a missing collection, and a missing collection is
   ///    the normal state on a first index, so the failure is expected rather than exceptional.
   /// </summary>
   private async Task<long> TryCountAsync(string collectionName, CancellationToken ct)
   {
      try
      {
         return await VectorStore.CountAsync(collectionName, ct).ConfigureAwait(false);
      }
      catch (Exception)
      {
         // Collection absent (or momentarily unreachable). Indexing below reports the real failure.
         return -1;
      }
   }

   /// <summary>
   ///    Derives a stable, UUID-shaped point id so re-indexing replaces points instead of appending them.
   /// </summary>
   /// <remarks>
   ///    The result must parse as a <see cref="Guid" />: both stores fall back to a freshly generated
   ///    GUID when the supplied id does not parse (<c>QdrantVectorStore</c> point id, <c>PgVectorStore</c>
   ///    id column), which would silently reintroduce the duplication this scheme exists to remove.
   ///    <para>
   ///       When the caller supplied a stable source identity the id is derived from position, so an
   ///       edited chunk overwrites its own point instead of orphaning it. Without any source identity
   ///       the id falls back to the chunk's own content hash, which at least makes re-indexing the same
   ///       text idempotent.
   ///    </para>
   /// </remarks>
   private static string ComputePointId(
      string collectionName,
      IReadOnlyDictionary<string, string>? metadata,
      int chunkIndex,
      string chunkText)
   {
      var sourceKey = ResolveSourceKey(metadata);
      Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
      var payload = sourceKey.Length > 0
         ? $"{collectionName}{sourceKey}{chunkIndex}"
         : $"{collectionName}{chunkText}";

      SHA256.HashData(Encoding.UTF8.GetBytes(payload), hash);
      return new Guid(hash[..16]).ToString();
   }

   /// <summary>
   ///    Resolves the caller-supplied source identity, preferring the full path over the bare file name
   ///    so two identically named files in different folders do not collide.
   /// </summary>
   private static string ResolveSourceKey(IReadOnlyDictionary<string, string>? metadata)
   {
      if (metadata is null)
         return string.Empty;

      if (metadata.TryGetValue("source_path", out var path) && !string.IsNullOrWhiteSpace(path))
         return path;

      if (metadata.TryGetValue("source", out var source) && !string.IsNullOrWhiteSpace(source))
         return source;

      return string.Empty;
   }

   /// <inheritdoc />
   public virtual async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
      string collectionName,
      string query,
      int limit = 5,
      float scoreThreshold = 0.0f,
      CancellationToken ct = default)
   {
      var queryVector = await EmbeddingProvider.EmbedAsync(query, ct).ConfigureAwait(false);
      return await VectorStore.SearchAsync(collectionName, queryVector, limit, scoreThreshold, ct).ConfigureAwait(false);
   }

   /// <inheritdoc />
   public virtual async Task<string> GetContextAsync(
      string collectionName,
      string query,
      int limit = 5,
      CancellationToken ct = default)
   {
      var results = await SearchAsync(collectionName, query, limit, ct: ct);
      return RagContextFormatter.Format(results);
   }

   /// <inheritdoc />
   public virtual ValueTask DisposeAsync()
   {
      return VectorStore.DisposeAsync();
   }
}
