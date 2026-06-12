using System.Diagnostics;

namespace Delibera.Core.Compression;

/// <summary>
/// Summarization compression strategy — uses an LLM to produce a concise summary
/// of the input text, preserving key facts and arguments.
/// </summary>
/// <remarks>
/// <para>This is the most aggressive compression strategy and can achieve very high
/// compression ratios (e.g., 0.1–0.3). However, it requires an LLM call per
/// compression operation, adding latency and cost.</para>
/// <para>Best used for compressing large accumulated context between debate rounds.</para>
/// </remarks>
public sealed class SummarizationCompressor : IContextCompressor
{
   private readonly ILLMProvider _llmProvider;
   private readonly string _modelName;

   /// <inheritdoc/>
   public string StrategyName => "Summarization";

   /// <inheritdoc/>
   public string Description => "Uses an LLM to produce a concise summary preserving key facts.";

   /// <summary>
   /// Creates a summarization compressor.
   /// </summary>
   /// <param name="llmProvider">LLM provider for generating summaries.</param>
   /// <param name="modelName">Model name to use for summarization.</param>
   public SummarizationCompressor(ILLMProvider llmProvider, string modelName)
   {
      _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
      _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
   }

   /// <inheritdoc/>
   public async Task<CompressedContext> CompressAsync(string text, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var sw = Stopwatch.StartNew();
      options ??= CompressionOptions.Default;
      var counter = TokenCounter.Default;
      var originalTokens = counter.EstimateTokens(text);

      if (originalTokens <= 50) // Too short to summarize
         return PassThrough(text, originalTokens, sw.Elapsed);

      var targetTokens = options.MaxOutputTokens ?? (int)(originalTokens * options.TargetRatio);

      var systemPrompt = """
            You are a precision text compressor. Your task is to compress the given text
            while preserving ALL key facts, arguments, data points, and conclusions.
            
            Rules:
            1. Preserve all factual claims and evidence
            2. Keep the logical structure of arguments
            3. Remove redundancy, filler words, and verbose phrasing
            4. Maintain technical accuracy — do not alter meaning
            5. If code blocks are present, keep them verbatim
            6. Output ONLY the compressed text — no explanations or meta-commentary
            """;

      var userPrompt = $"""
            Compress the following text to approximately {targetTokens} tokens
            (roughly {(int)(targetTokens * 4)} characters).
            Preserve all key information.

            ---
            {text}
            ---

            Compressed version:
            """;

      var summary = await _llmProvider.ChatAsync(
          _modelName, systemPrompt, userPrompt,
          options.SummarizationTemperature, ct);

      var compressedTokens = counter.EstimateTokens(summary);

      sw.Stop();
      return new CompressedContext
      {
         Text = summary,
         OriginalLength = text.Length,
         CompressedLength = summary.Length,
         OriginalTokens = originalTokens,
         CompressedTokens = compressedTokens,
         StrategyUsed = StrategyName,
         Duration = sw.Elapsed
      };
   }

   /// <inheritdoc/>
   public async Task<CompressedContext> CompressBatchAsync(IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var sw = Stopwatch.StartNew();
      options ??= CompressionOptions.Default;
      var counter = TokenCounter.Default;

      var merged = string.Join("\n\n---\n\n", texts);
      var originalTokens = counter.EstimateTokens(merged);
      var targetTokens = options.MaxOutputTokens ?? (int)(originalTokens * options.TargetRatio);

      var systemPrompt = """
            You are a precision text compressor. You are given multiple text sections
            separated by '---'. Merge and compress them into a single coherent summary.
            
            Rules:
            1. Identify and merge overlapping information across sections
            2. Preserve ALL unique facts, arguments, and conclusions
            3. Remove cross-section duplication
            4. Maintain a logical flow
            5. Output ONLY the compressed text
            """;

      var userPrompt = $"""
            Merge and compress these {texts.Count} sections to approximately {targetTokens} tokens:

            {merged}

            Merged compressed version:
            """;

      var summary = await _llmProvider.ChatAsync(
          _modelName, systemPrompt, userPrompt,
          options.SummarizationTemperature, ct);

      var compressedTokens = counter.EstimateTokens(summary);

      sw.Stop();
      return new CompressedContext
      {
         Text = summary,
         OriginalLength = merged.Length,
         CompressedLength = summary.Length,
         OriginalTokens = originalTokens,
         CompressedTokens = compressedTokens,
         StrategyUsed = StrategyName,
         Duration = sw.Elapsed
      };
   }

   private static CompressedContext PassThrough(string text, int tokens, TimeSpan duration) => new()
   {
      Text = text,
      OriginalLength = text.Length,
      CompressedLength = text.Length,
      OriginalTokens = tokens,
      CompressedTokens = tokens,
      StrategyUsed = "Summarization",
      Duration = duration
   };
}
