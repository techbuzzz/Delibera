namespace Delibera.Core.Compression;

/// <summary>
///    Injectable service that delegates to <see cref="CompressionFactory" /> static methods.
///    Implements <see cref="ICompressionFactory" /> for dependency injection scenarios.
/// </summary>
/// <remarks>
///    The static <see cref="CompressionFactory" /> class remains available for direct use
///    without DI — this service wraps it for IoC container registration.
/// </remarks>
public sealed class CompressionService : ICompressionFactory
{
   /// <inheritdoc />
   public IContextCompressor Create(
      CompressionStrategy strategy,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null) =>
      CompressionFactory.Create(strategy, llmProvider, modelName, embeddingProvider);

   /// <inheritdoc />
   public IContextCompressor Create(
      string strategyName,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null) =>
      CompressionFactory.Create(strategyName, llmProvider, modelName, embeddingProvider);
}
