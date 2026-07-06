namespace Delibera.Core.Interfaces;

/// <summary>
///    A single point to be stored in the vector database.
/// </summary>
/// <param name="Id">Unique point identifier.</param>
/// <param name="Vector">Embedding vector.</param>
/// <param name="Text">Original text chunk.</param>
/// <param name="Metadata">Optional key-value metadata (source file, page, etc.).</param>
public sealed record VectorPoint(
   string Id,
   float[] Vector,
   string Text,
   Dictionary<string, string>? Metadata = null);

/// <summary>
///    A scored result returned from a vector similarity search.
/// </summary>
/// <param name="Id">Point identifier.</param>
/// <param name="Text">Original text chunk.</param>
/// <param name="Score">Similarity score (higher = more similar).</param>
/// <param name="Metadata">Point metadata.</param>
public sealed record VectorSearchResult(
   string Id,
   string Text,
   float Score,
   Dictionary<string, string>? Metadata = null);

/// <summary>
///    Low-level vector storage abstraction — stores, retrieves and searches vectors.
///    Implement for Qdrant, pgvector, Pinecone, etc.
/// </summary>
public interface IVectorStore : IAsyncDisposable
{
   /// <summary>Name of the underlying vector database.</summary>
   string StoreName { get; }

   /// <summary>Ensures the collection / index exists, creating it if necessary.</summary>
   /// <param name="collectionName">Collection name.</param>
   /// <param name="vectorSize">Expected embedding dimensionality.</param>
   /// <param name="ct">Cancellation token.</param>
   Task EnsureCollectionAsync(string collectionName, int vectorSize, CancellationToken ct = default);

   /// <summary>
   ///    Upserts a batch of vectors with associated payloads.
   /// </summary>
   /// <param name="collectionName">Target collection.</param>
   /// <param name="points">Points to upsert.</param>
   /// <param name="ct">Cancellation token.</param>
   Task UpsertAsync(string collectionName, IReadOnlyList<VectorPoint> points, CancellationToken ct = default);

   /// <summary>
   ///    Searches for the nearest neighbours of the given query vector.
   /// </summary>
   /// <param name="collectionName">Collection to search in.</param>
   /// <param name="queryVector">Query embedding.</param>
   /// <param name="limit">Maximum number of results.</param>
   /// <param name="scoreThreshold">Minimum similarity score (0..1). May be ignored if not supported.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Scored search results.</returns>
   Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
      string collectionName,
      float[] queryVector,
      int limit = 5,
      float scoreThreshold = 0.0f,
      CancellationToken ct = default);

   /// <summary>Deletes the entire collection.</summary>
   Task DeleteCollectionAsync(string collectionName, CancellationToken ct = default);

   /// <summary>Returns the total number of points in a collection.</summary>
   Task<long> CountAsync(string collectionName, CancellationToken ct = default);
}
