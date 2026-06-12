namespace Delibera.Core.Providers.RAG;

/// <summary>
///    Shared base for vector-store backed RAG providers. Holds the embedding provider,
///    the vector store, the chunking logic and the convenience text-formatter used by
///    <see cref="GetContextAsync" />. Subclasses provide only the constructor that
///    wires up the specific <see cref="IVectorStore" />.
/// </summary>
public abstract class BaseRagProvider : IRagProvider
{
   /// <summary>Abstract <see cref="IRagProvider" /> implementations describe themselves.</summary>
   public abstract string ProviderName { get; }

   /// <inheritdoc />
   public IVectorStore VectorStore { get; }

   /// <inheritdoc />
   public IEmbeddingProvider EmbeddingProvider { get; }

   /// <summary>
   ///    Initialises a base RAG provider with the supplied vector store and embedding provider.
   /// </summary>
   protected BaseRagProvider(IVectorStore vectorStore, IEmbeddingProvider embeddingProvider)
   {
      VectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
      EmbeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
   }

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
      var vectors = await EmbeddingProvider.EmbedBatchAsync(chunks, ct);

      // Ensure collection/table exists
      await VectorStore.EnsureCollectionAsync(collectionName, vectors[0].Length, ct);

      // Build points
      var points = new List<VectorPoint>(chunks.Count);
      for (var i = 0; i < chunks.Count; i++)
      {
         var pointMeta = metadata is not null
            ? new Dictionary<string, string>(metadata)
            : new Dictionary<string, string>();
         pointMeta["chunk_index"] = i.ToString();

         points.Add(new VectorPoint(
            Guid.NewGuid().ToString(),
            vectors[i],
            chunks[i],
            pointMeta));
      }

      await VectorStore.UpsertAsync(collectionName, points, ct);
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

      var text = await File.ReadAllTextAsync(fullPath, ct);
      var meta = new Dictionary<string, string>
      {
         ["source"] = Path.GetFileName(fullPath),
         ["source_path"] = fullPath
      };

      return await IndexDocumentAsync(collectionName, text, meta, chunkSize, chunkOverlap, ct);
   }

   /// <inheritdoc />
   public virtual async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
      string collectionName,
      string query,
      int limit = 5,
      float scoreThreshold = 0.0f,
      CancellationToken ct = default)
   {
      var queryVector = await EmbeddingProvider.EmbedAsync(query, ct);
      return await VectorStore.SearchAsync(collectionName, queryVector, limit, scoreThreshold, ct);
   }

   /// <inheritdoc />
   public virtual async Task<string> GetContextAsync(
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

   /// <inheritdoc />
   public virtual ValueTask DisposeAsync() => VectorStore.DisposeAsync();
}
