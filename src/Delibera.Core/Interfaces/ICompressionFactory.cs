namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for creating <see cref="IContextCompressor" /> instances.
///    Provides a non-static, injectable alternative to <c>CompressionFactory</c>.
/// </summary>
public interface ICompressionFactory
{
   /// <summary>
   ///    Creates a compressor for the specified strategy.
   /// </summary>
   /// <param name="strategy">Compression strategy to use.</param>
   /// <param name="llmProvider">LLM provider (required for Summarization/Hybrid strategies).</param>
   /// <param name="modelName">Model name (required for Summarization/Hybrid strategies).</param>
   /// <param name="embeddingProvider">Embedding provider (required for Semantic/Hybrid strategies).</param>
   /// <returns>A configured <see cref="IContextCompressor" />.</returns>
   IContextCompressor Create(
      CompressionStrategy strategy,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null);

   /// <summary>
   ///    Creates a compressor from a strategy name string (case-insensitive).
   /// </summary>
   /// <param name="strategyName">Strategy name (e.g., "Hybrid", "Semantic").</param>
   /// <param name="llmProvider">LLM provider (required for Summarization/Hybrid strategies).</param>
   /// <param name="modelName">Model name (required for Summarization/Hybrid strategies).</param>
   /// <param name="embeddingProvider">Embedding provider (required for Semantic/Hybrid strategies).</param>
   /// <returns>A configured <see cref="IContextCompressor" />.</returns>
   IContextCompressor Create(
      string strategyName,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null);
}