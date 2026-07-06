namespace Delibera.Core.Interfaces;

/// <summary>
///    Strategy for compressing context before sending to LLMs.
///    Reduces token usage while preserving semantic meaning.
/// </summary>
public interface IContextCompressor
{
   /// <summary>Unique name of this compression strategy.</summary>
   string StrategyName { get; }

   /// <summary>Human-readable description of the compression approach.</summary>
   string Description { get; }

   /// <summary>
   ///    Compresses the input text according to the configured strategy.
   /// </summary>
   /// <param name="text">Original text to compress.</param>
   /// <param name="options">Compression options controlling behaviour.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Compressed context with metadata about the compression.</returns>
   Task<CompressedContext> CompressAsync(string text, CompressionOptions? options = null, CancellationToken ct = default);

   /// <summary>
   ///    Compresses multiple texts and merges the results.
   /// </summary>
   /// <param name="texts">Texts to compress (e.g., round responses).</param>
   /// <param name="options">Compression options.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Compressed and merged context.</returns>
   Task<CompressedContext> CompressBatchAsync(IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default);
}

/// <summary>
///    Enumeration of available compression strategies.
/// </summary>
public enum CompressionStrategy
{
   /// <summary>No compression — pass-through.</summary>
   None = 0,

   /// <summary>Semantic compression — ranks sentences by importance and keeps the most relevant.</summary>
   Semantic,

   /// <summary>Deduplication — removes semantically similar / duplicate content.</summary>
   Deduplication,

   /// <summary>Summarization — uses an LLM to produce a concise summary.</summary>
   Summarization,

   /// <summary>Hybrid — combines deduplication + semantic ranking + optional summarization.</summary>
   Hybrid
}

/// <summary>
///    Options controlling context compression behaviour.
/// </summary>
public sealed record CompressionOptions
{
   /// <summary>
   ///    Target compression ratio (0.1–1.0). For example, 0.5 = try to reduce to 50% of original.
   ///    Not all strategies can hit the target exactly; this is a best-effort hint.
   /// </summary>
   public double TargetRatio { get; init; } = 0.5;

   /// <summary>
   ///    Maximum output token count. If set, overrides <see cref="TargetRatio" />.
   /// </summary>
   public int? MaxOutputTokens { get; init; }

   /// <summary>
   ///    Whether to preserve code blocks and structured data verbatim.
   /// </summary>
   public bool PreserveCodeBlocks { get; init; } = true;

   /// <summary>
   ///    Whether to preserve bullet-point lists and tables.
   /// </summary>
   public bool PreserveStructuredContent { get; init; } = true;

   /// <summary>
   ///    Minimum similarity threshold for deduplication (0.0–1.0).
   ///    Sentences with similarity above this threshold are considered duplicates.
   /// </summary>
   public double DeduplicationThreshold { get; init; } = 0.85;

   /// <summary>
   ///    Temperature for summarisation LLM calls.
   /// </summary>
   public float SummarizationTemperature { get; init; } = 0.3f;

   /// <summary>
   ///    Creates default compression options.
   /// </summary>
   public static CompressionOptions Default { get; } = new();
}

/// <summary>
///    Result of a context compression operation.
/// </summary>
/// <remarks>
///    Contains the compressed text along with statistics about the compression.
/// </remarks>
public sealed record CompressedContext
{
   /// <summary>The compressed text.</summary>
   public required string Text { get; init; }

   /// <summary>Original text length in characters.</summary>
   public required int OriginalLength { get; init; }

   /// <summary>Compressed text length in characters.</summary>
   public required int CompressedLength { get; init; }

   /// <summary>Estimated original token count.</summary>
   public required int OriginalTokens { get; init; }

   /// <summary>Estimated compressed token count.</summary>
   public required int CompressedTokens { get; init; }

   /// <summary>Name of the strategy that produced this result.</summary>
   public required string StrategyUsed { get; init; }

   /// <summary>Time taken to perform the compression.</summary>
   public TimeSpan Duration { get; init; }

   /// <summary>Compression ratio (0.0–1.0; lower = more compressed).</summary>
   public double CompressionRatio => OriginalTokens > 0
      ? (double)CompressedTokens / OriginalTokens
      : 1.0;

   /// <summary>Percentage of tokens saved.</summary>
   public double TokensSavedPercent => (1.0 - CompressionRatio) * 100.0;
}
