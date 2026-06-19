namespace Delibera.Core.Compression;

/// <summary>
///    Factory for creating <see cref="IContextCompressor" /> instances by strategy type.
/// </summary>
public static class CompressionFactory
{
   /// <summary>
   ///    Creates a compressor for the specified strategy.
   /// </summary>
   /// <param name="strategy">Compression strategy to use.</param>
   /// <param name="llmProvider">
   ///    LLM provider required for summarization strategies. May be <c>null</c> for non-LLM
   ///    strategies.
   /// </param>
   /// <param name="modelName">Model name for LLM-based compression. May be <c>null</c> for non-LLM strategies.</param>
   /// <param name="embeddingProvider">
   ///    Embedding provider for semantic strategies. May be <c>null</c> for non-embedding
   ///    strategies.
   /// </param>
   /// <returns>A configured <see cref="IContextCompressor" />.</returns>
   /// <exception cref="ArgumentException">If the strategy requires dependencies that were not provided.</exception>
   public static IContextCompressor Create(
      CompressionStrategy strategy,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null)
   {
      return strategy switch
      {
         CompressionStrategy.None => PassThroughCompressor.Instance,
         CompressionStrategy.Semantic => new SemanticCompressor(
            embeddingProvider ?? throw new ArgumentException("Semantic compression requires an IEmbeddingProvider.", nameof(embeddingProvider))),
         CompressionStrategy.Deduplication => new DeduplicationCompressor(embeddingProvider),
         CompressionStrategy.Summarization => new SummarizationCompressor(
            llmProvider ?? throw new ArgumentException("Summarization compression requires an ILLMProvider.", nameof(llmProvider)),
            modelName ?? throw new ArgumentException("Summarization compression requires a modelName.", nameof(modelName))),
         CompressionStrategy.Hybrid => new HybridCompressor(llmProvider, modelName, embeddingProvider),
         _ => throw new ArgumentOutOfRangeException(nameof(strategy), $"Unknown compression strategy: {strategy}")
      };
   }

   /// <summary>
   ///    Creates a compressor from a strategy name string (case-insensitive).
   /// </summary>
   public static IContextCompressor Create(
      string strategyName,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null)
   {
      if (!Enum.TryParse<CompressionStrategy>(strategyName, true, out var strategy))
         throw new ArgumentException($"Unknown compression strategy: '{strategyName}'. Available: {string.Join(", ", Enum.GetNames<CompressionStrategy>())}");

      return Create(strategy, llmProvider, modelName, embeddingProvider);
   }
}

/// <summary>
///    No-op compressor — passes text through unchanged. Used when compression is disabled.
/// </summary>
internal sealed class PassThroughCompressor : IContextCompressor
{
   public static readonly PassThroughCompressor Instance = new();

   private PassThroughCompressor()
   {
   }

   /// <inheritdoc />
   public string StrategyName => "None";

   /// <inheritdoc />
   public string Description => "No compression — pass-through";

   /// <inheritdoc />
   public Task<CompressedContext> CompressAsync(string text, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var tokens = TokenCounter.Default.EstimateTokens(text);
      return Task.FromResult(CompressedContextFactory.PassThrough(
         text, tokens, StrategyName, TimeSpan.Zero));
   }

   /// <inheritdoc />
   public Task<CompressedContext> CompressBatchAsync(IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var merged = string.Join("\n\n", texts);
      return CompressAsync(merged, options, ct);
   }
}