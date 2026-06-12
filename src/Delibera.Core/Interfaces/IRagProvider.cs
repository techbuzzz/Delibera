namespace Delibera.Core.Interfaces;

/// <summary>
/// High-level RAG (Retrieval Augmented Generation) provider.
/// Combines an <see cref="IEmbeddingProvider"/>, an <see cref="IVectorStore"/>
/// and optional text-splitting logic.
/// </summary>
public interface IRagProvider : IAsyncDisposable
{
   /// <summary>Name of this RAG provider instance.</summary>
   string ProviderName { get; }

   /// <summary>The underlying vector store.</summary>
   IVectorStore VectorStore { get; }

   /// <summary>The embedding provider used for vectorisation.</summary>
   IEmbeddingProvider EmbeddingProvider { get; }

   /// <summary>
   /// Indexes a single document, splitting it into chunks and storing embeddings.
   /// </summary>
   /// <param name="collectionName">Target collection.</param>
   /// <param name="documentText">Full document text.</param>
   /// <param name="metadata">Optional metadata to attach to every chunk.</param>
   /// <param name="chunkSize">Approximate chunk size in characters.</param>
   /// <param name="chunkOverlap">Overlap between consecutive chunks.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Number of indexed chunks.</returns>
   Task<int> IndexDocumentAsync(
       string collectionName,
       string documentText,
       Dictionary<string, string>? metadata = null,
       int chunkSize = 500,
       int chunkOverlap = 50,
       CancellationToken ct = default);

   /// <summary>
   /// Indexes a file from disk (reads, splits, embeds, stores).
   /// </summary>
   Task<int> IndexFileAsync(
       string collectionName,
       string filePath,
       int chunkSize = 500,
       int chunkOverlap = 50,
       CancellationToken ct = default);

   /// <summary>
   /// Performs a semantic search and returns relevant text chunks.
   /// </summary>
   /// <param name="collectionName">Collection to search.</param>
   /// <param name="query">Natural language query.</param>
   /// <param name="limit">Maximum number of results.</param>
   /// <param name="scoreThreshold">Minimum similarity score.</param>
   /// <param name="ct">Cancellation token.</param>
   Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
       string collectionName,
       string query,
       int limit = 5,
       float scoreThreshold = 0.0f,
       CancellationToken ct = default);

   /// <summary>
   /// Convenience method: searches for context and concatenates results
   /// into a single string suitable for injection into an LLM prompt.
   /// </summary>
   Task<string> GetContextAsync(
       string collectionName,
       string query,
       int limit = 5,
       CancellationToken ct = default);
}
