namespace Delibera.Core.Interfaces;

/// <summary>
/// Generates vector embeddings for text chunks.
/// Used by <see cref="IRagProvider"/> for indexing and querying.
/// </summary>
public interface IEmbeddingProvider
{
   /// <summary>Name of the embedding provider / model.</summary>
   string EmbeddingModelName { get; }

   /// <summary>Dimensionality of the output vectors.</summary>
   int VectorSize { get; }

   /// <summary>
   /// Computes an embedding vector for a single text.
   /// </summary>
   Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

   /// <summary>
   /// Computes embedding vectors for multiple texts in a single batch.
   /// </summary>
   Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
