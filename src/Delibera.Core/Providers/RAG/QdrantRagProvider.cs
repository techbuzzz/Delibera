namespace Delibera.Core.Providers.RAG;

/// <summary>
/// RAG provider backed by Qdrant vector database.
/// Combines an <see cref="IEmbeddingProvider"/> with a <see cref="QdrantVectorStore"/>
/// to index documents and perform semantic search.
/// </summary>
public sealed class QdrantRagProvider : IRagProvider
{
   /// <inheritdoc/>
   public string ProviderName => "QdrantRAG";

   /// <inheritdoc/>
   public IVectorStore VectorStore { get; }

   /// <inheritdoc/>
   public IEmbeddingProvider EmbeddingProvider { get; }

   /// <summary>
   /// Creates a Qdrant RAG provider.
   /// </summary>
   /// <param name="vectorStore">Qdrant vector store instance.</param>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   public QdrantRagProvider(IVectorStore vectorStore, IEmbeddingProvider embeddingProvider)
   {
      VectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
      EmbeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
   }

   /// <summary>
   /// Convenience constructor that creates a <see cref="QdrantVectorStore"/> internally.
   /// </summary>
   public QdrantRagProvider(
       IEmbeddingProvider embeddingProvider,
       string qdrantHost = "localhost",
       int qdrantPort = 6334,
       bool https = false,
       string? apiKey = null)
       : this(new QdrantVectorStore(qdrantHost, qdrantPort, https, apiKey), embeddingProvider)
   {
   }

   /// <inheritdoc/>
   public async Task<int> IndexDocumentAsync(
       string collectionName,
       string documentText,
       Dictionary<string, string>? metadata = null,
       int chunkSize = 500,
       int chunkOverlap = 50,
       CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
      ArgumentException.ThrowIfNullOrWhiteSpace(documentText);

      var chunks = SplitIntoChunks(documentText, chunkSize, chunkOverlap);
      if (chunks.Count == 0) return 0;

      // Compute embeddings in batch
      var vectors = await EmbeddingProvider.EmbedBatchAsync(chunks, ct);

      // Ensure collection exists
      await VectorStore.EnsureCollectionAsync(collectionName, vectors[0].Length, ct);

      // Build points
      var points = new List<VectorPoint>(chunks.Count);
      for (var i = 0; i < chunks.Count; i++)
      {
         var pointMeta = metadata is not null ? new Dictionary<string, string>(metadata) : new();
         pointMeta["chunk_index"] = i.ToString();

         points.Add(new VectorPoint(
             Id: Guid.NewGuid().ToString(),
             Vector: vectors[i],
             Text: chunks[i],
             Metadata: pointMeta));
      }

      await VectorStore.UpsertAsync(collectionName, points, ct);
      return chunks.Count;
   }

   /// <inheritdoc/>
   public async Task<int> IndexFileAsync(
       string collectionName,
       string filePath,
       int chunkSize = 500,
       int chunkOverlap = 50,
       CancellationToken ct = default)
   {
      var fullPath = Path.GetFullPath(filePath);
      if (!File.Exists(fullPath))
         throw new FileNotFoundException($"File not found: {fullPath}");

      var text = await File.ReadAllTextAsync(fullPath, ct);
      var meta = new Dictionary<string, string>
      {
         ["source"] = Path.GetFileName(fullPath),
         ["source_path"] = fullPath
      };

      return await IndexDocumentAsync(collectionName, text, meta, chunkSize, chunkOverlap, ct);
   }

   /// <inheritdoc/>
   public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
       string collectionName,
       string query,
       int limit = 5,
       float scoreThreshold = 0.0f,
       CancellationToken ct = default)
   {
      var queryVector = await EmbeddingProvider.EmbedAsync(query, ct);
      return await VectorStore.SearchAsync(collectionName, queryVector, limit, scoreThreshold, ct);
   }

   /// <inheritdoc/>
   public async Task<string> GetContextAsync(
       string collectionName,
       string query,
       int limit = 5,
       CancellationToken ct = default)
   {
      var results = await SearchAsync(collectionName, query, limit, ct: ct);
      if (results.Count == 0)
         return string.Empty;

      var sb = new StringBuilder();
      for (var i = 0; i < results.Count; i++)
      {
         sb.AppendLine($"[Source {i + 1} — score: {results[i].Score:F3}]");
         sb.AppendLine(results[i].Text);
         sb.AppendLine();
      }

      return sb.ToString().TrimEnd();
   }

   /// <inheritdoc/>
   public async ValueTask DisposeAsync()
   {
      await VectorStore.DisposeAsync();
   }

   // ──────────────────────────────────────────────
   // Text chunking
   // ──────────────────────────────────────────────

   /// <summary>
   /// Splits text into overlapping chunks of approximately <paramref name="chunkSize"/> characters,
   /// breaking on paragraph or sentence boundaries where possible.
   /// </summary>
   internal static List<string> SplitIntoChunks(string text, int chunkSize, int chunkOverlap)
   {
      if (string.IsNullOrWhiteSpace(text)) return [];
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
      ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlap);
      if (chunkOverlap >= chunkSize)
         throw new ArgumentException("chunkOverlap must be smaller than chunkSize.", nameof(chunkOverlap));

      var separators = new[] { "\n\n", "\r\n\r\n", "\n", ". ", "! ", "? " };
      var chunks = new List<string>();
      var pos = 0;

      while (pos < text.Length)
      {
         var end = Math.Min(pos + chunkSize, text.Length);

         if (end < text.Length)
         {
            var minBreakPos = pos + Math.Max(1, chunkSize / 2);
            var bestBreak = -1;
            foreach (var sep in separators)
            {
               var searchStart = Math.Min(end, text.Length);
               var idx = text.LastIndexOf(sep, searchStart, searchStart - pos, StringComparison.Ordinal);
               if (idx >= minBreakPos && idx > bestBreak)
               {
                  bestBreak = idx + sep.Length;
                  break;
               }
            }

            if (bestBreak > pos)
               end = bestBreak;
         }

         var chunk = text[pos..end].Trim();
         if (chunk.Length > 0)
            chunks.Add(chunk);

         if (end >= text.Length) break;

         var nextPos = end - chunkOverlap;
         if (nextPos <= pos) nextPos = pos + 1;
         if (nextPos >= text.Length) break;
         pos = nextPos;
      }

      return chunks;
   }
}
