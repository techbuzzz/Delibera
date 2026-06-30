namespace Delibera.Core.Knowledge;

/// <summary>
///    File-based knowledge base that loads Markdown files and provides
///    keyword search across their content.
/// </summary>
public sealed class MarkdownKnowledgeBase : IKnowledgeBase
{
   private readonly Dictionary<string, string> _documents = new(StringComparer.OrdinalIgnoreCase);
   private readonly Dictionary<string, IReadOnlyDictionary<string, string>?> _metadata = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>Creates a Markdown knowledge base with an optional name.</summary>
   public MarkdownKnowledgeBase(string name = "Markdown Knowledge Base")
   {
      Name = name;
   }

   /// <summary>Number of loaded documents.</summary>
   public int DocumentCount => _documents.Count;

   /// <summary>Total characters across all documents.</summary>
   public int TotalCharacters => _documents.Values.Sum(d => d.Length);

   /// <summary>
   ///    Snapshot of per-source metadata captured at load time.
   ///    Keys are source names; values are the metadata dictionaries
   ///    supplied via <see cref="LoadTextAsync(KnowledgeDocument, CancellationToken)"/>
   ///    (or an empty/null entry for sources loaded through the file-path API).
   /// </summary>
   public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>?> DocumentMetadata
      => _metadata;

   /// <inheritdoc />
   public string Name { get; }

   /// <inheritdoc />
   public async Task LoadAsync(string source, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(source);
      ct.ThrowIfCancellationRequested();
      var fullPath = Path.GetFullPath(source);
      if (!File.Exists(fullPath))
         throw new FileNotFoundException($"Knowledge file not found: {fullPath}");

      var content = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
      Store(Path.GetFileName(fullPath), content, metadata: null);
   }

   /// <inheritdoc />
   public async Task LoadManyAsync(IEnumerable<string> sources, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(sources);
      foreach (var s in sources)
      {
         ct.ThrowIfCancellationRequested();
         await LoadAsync(s, ct).ConfigureAwait(false);
      }
   }

   /// <summary>
   ///    Loads all matching files from a directory.
   /// </summary>
   /// <param name="directoryPath">Directory to scan recursively.</param>
   /// <param name="pattern">File-name pattern (default: <c>*.md</c>).</param>
   /// <param name="ct">Cancellation token; checked between files.</param>
   /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   public async Task LoadDirectoryAsync(string directoryPath, string pattern = "*.md", CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
      ct.ThrowIfCancellationRequested();
      var full = Path.GetFullPath(directoryPath);
      if (!Directory.Exists(full))
         throw new DirectoryNotFoundException($"Directory not found: {full}");

      await LoadManyAsync(Directory.GetFiles(full, pattern, SearchOption.AllDirectories), ct).ConfigureAwait(false);
   }

   /// <summary>
   ///    Ingest a single markdown body into the KB.
   ///    Equivalent to writing the body to a temp file and calling
   ///    <see cref="LoadAsync(string, CancellationToken)"/>, but without the disk I/O.
   ///    The <paramref name="sourceName"/> appears in the council's
   ///    per-round context, just like a file path would.
   /// </summary>
   /// <param name="content">Markdown body. Required, non-empty.</param>
   /// <param name="sourceName">Display name for the document. Required, non-empty.</param>
   /// <param name="ct">Cancellation token; honors cooperative cancellation at entry and during indexing.</param>
   /// <exception cref="ArgumentException"><paramref name="content"/> or <paramref name="sourceName"/> is null or whitespace.</exception>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   public async Task LoadTextAsync(
      string content,
      string sourceName,
      CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(content);
      ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
      ct.ThrowIfCancellationRequested();

      await LoadCoreAsync(content, sourceName, metadata: null, ct).ConfigureAwait(false);
   }

   /// <summary>
   ///    Ingest a single <see cref="KnowledgeDocument"/>. The
   ///    <c>Metadata</c> dictionary is preserved on the indexed
   ///    document (see <see cref="DocumentMetadata"/>) and may surface
   ///    to the chairman as additional per-source context.
   /// </summary>
   /// <param name="document">The document to ingest. Required.</param>
   /// <param name="ct">Cancellation token; honors cooperative cancellation at entry and during indexing.</param>
   /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
   /// <exception cref="ArgumentException"><see cref="KnowledgeDocument.Name"/> or <see cref="KnowledgeDocument.Content"/> is null or whitespace.</exception>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   public async Task LoadTextAsync(
      KnowledgeDocument document,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(document);
      ArgumentException.ThrowIfNullOrWhiteSpace(document.Name);
      ArgumentException.ThrowIfNullOrWhiteSpace(document.Content);
      ct.ThrowIfCancellationRequested();

      await LoadCoreAsync(document.Content, document.Name, document.Metadata, ct).ConfigureAwait(false);
   }

   /// <summary>
   ///    Bulk ingest. Equivalent to calling
   ///    <see cref="LoadTextAsync(KnowledgeDocument, CancellationToken)"/>
   ///    for each document sequentially, with cooperative cancellation
   ///    checked between documents.
   /// </summary>
   /// <param name="documents">The documents to ingest. Required.</param>
   /// <param name="ct">Cancellation token; checked before each document is loaded.</param>
   /// <exception cref="ArgumentNullException"><paramref name="documents"/> is null.</exception>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   public async Task LoadTextsAsync(
      IEnumerable<KnowledgeDocument> documents,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(documents);
      foreach (var doc in documents)
      {
         ct.ThrowIfCancellationRequested();
         await LoadTextAsync(doc, ct).ConfigureAwait(false);
      }
   }

   /// <summary>
   ///    Shared indexing path for the file-based and in-memory loaders.
   ///    Stores <paramref name="content"/> under <paramref name="sourceName"/>
   ///    together with optional <paramref name="metadata"/>.
   /// </summary>
   /// <param name="content">Markdown body. Caller has validated non-whitespace.</param>
   /// <param name="sourceName">Display name. Caller has validated non-whitespace.</param>
   /// <param name="metadata">Optional metadata to tag the document. May be null.</param>
   /// <param name="ct">Cancellation token; checked before the indexing work.</param>
   private Task LoadCoreAsync(
      string content,
      string sourceName,
      IReadOnlyDictionary<string, string>? metadata,
      CancellationToken ct)
   {
      ct.ThrowIfCancellationRequested();
      Store(sourceName, content, metadata);
      return Task.CompletedTask;
   }

   private void Store(
      string sourceName,
      string content,
      IReadOnlyDictionary<string, string>? metadata)
   {
      _documents[sourceName] = content;
      _metadata[sourceName] = metadata;
   }

   /// <inheritdoc />
   public string GetAllContent()
   {
      if (_documents.Count == 0) return string.Empty;
      var sb = new StringBuilder();
      foreach (var (name, content) in _documents)
      {
         sb.AppendLine($"--- [{name}] ---");
         sb.AppendLine(content);
         sb.AppendLine();
      }

      return sb.ToString();
   }

   /// <inheritdoc />
   public IReadOnlyList<string> Search(string query, int maxResults = 5)
   {
      ArgumentNullException.ThrowIfNull(query);
      if (string.IsNullOrWhiteSpace(query)) return [];

      var keywords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var scored = new List<(string chunk, int score)>();

      foreach (var (fileName, content) in _documents)
      {
         var paragraphs = content.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
         foreach (var p in paragraphs)
         {
            var lower = p.ToLowerInvariant();
            var score = keywords.Count(k => lower.Contains(k));
            if (score > 0)
               scored.Add(($"[{fileName}] {p.Trim()}", score));
         }
      }

      return scored
         .OrderByDescending(r => r.score)
         .Take(maxResults)
         .Select(r => r.chunk)
         .ToList()
         .AsReadOnly();
   }

   /// <inheritdoc />
   public IReadOnlyList<string> GetLoadedSources()
   {
      return _documents.Keys.ToList().AsReadOnly();
   }
}