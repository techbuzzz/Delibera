using System.Diagnostics;

namespace Delibera.Core.Compression;

/// <summary>
///    Hybrid compression strategy — combines deduplication, semantic ranking,
///    and optional LLM summarization for maximum compression with high fidelity.
/// </summary>
/// <remarks>
///    <para>Pipeline:</para>
///    <list type="number">
///       <item><b>Stage 1 — Deduplication:</b> Remove duplicate and near-duplicate sentences.</item>
///       <item>
///          <b>Stage 2 — Semantic Ranking:</b> If embeddings available, rank remaining sentences by importance and trim
///          to target.
///       </item>
///       <item>
///          <b>Stage 3 — Summarization (optional):</b> If LLM available and result still exceeds target, summarize the
///          remainder.
///       </item>
///    </list>
///    <para>
///       This strategy provides the best balance of compression quality and efficiency.
///       If some dependencies are missing, the pipeline gracefully degrades to use available stages only.
///    </para>
/// </remarks>
public sealed class HybridCompressor : IContextCompressor
{
   private readonly IEmbeddingProvider? _embeddingProvider;
   private readonly ILLMProvider? _llmProvider;
   private readonly string? _modelName;

   /// <summary>
   ///    Creates a hybrid compressor.
   /// </summary>
   /// <param name="llmProvider">LLM provider for summarization stage (optional).</param>
   /// <param name="modelName">Model name for summarization (optional).</param>
   /// <param name="embeddingProvider">Embedding provider for semantic stages (optional).</param>
   public HybridCompressor(
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null)
   {
      _llmProvider = llmProvider;
      _modelName = modelName;
      _embeddingProvider = embeddingProvider;
   }

   /// <inheritdoc />
   public string StrategyName => "Hybrid";

   /// <inheritdoc />
   public string Description => "Multi-stage: Deduplication → Semantic Ranking → Optional Summarization.";

   /// <inheritdoc />
   public async Task<CompressedContext> CompressAsync(string text, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var sw = Stopwatch.StartNew();
      options ??= CompressionOptions.Default;
      var counter = TokenCounter.Default;
      var originalTokens = counter.EstimateTokens(text);
      var targetTokens = options.MaxOutputTokens ?? (int)(originalTokens * options.TargetRatio);

      if (originalTokens <= targetTokens || originalTokens <= 50)
         return CompressedContextFactory.PassThrough(text, originalTokens, StrategyName, sw.Elapsed);

      var currentText = text;

      // ── Stage 1: Deduplication ──
      var dedup = new DeduplicationCompressor(_embeddingProvider);
      var dedupResult = await dedup.CompressAsync(currentText, options, ct);
      currentText = dedupResult.Text;

      var currentTokens = counter.EstimateTokens(currentText);
      if (currentTokens <= targetTokens)
         return CompressedContextFactory.Compressed(text, currentText, originalTokens, currentTokens, StrategyName, sw.Elapsed);

      // ── Stage 2: Semantic Ranking (if embeddings available) ──
      if (_embeddingProvider is not null)
      {
         var semanticOptions = options with { MaxOutputTokens = targetTokens };
         var semantic = new SemanticCompressor(_embeddingProvider);
         var semanticResult = await semantic.CompressAsync(currentText, semanticOptions, ct);
         currentText = semanticResult.Text;

         currentTokens = counter.EstimateTokens(currentText);
         if (currentTokens <= targetTokens)
            return CompressedContextFactory.Compressed(text, currentText, originalTokens, currentTokens, StrategyName, sw.Elapsed);
      }

      // ── Stage 3: LLM Summarization (if available and still over target) ──
      if (_llmProvider is not null && _modelName is not null)
      {
         var summOptions = options with { MaxOutputTokens = targetTokens };
         var summarizer = new SummarizationCompressor(_llmProvider, _modelName);
         var summResult = await summarizer.CompressAsync(currentText, summOptions, ct);
         currentText = summResult.Text;
         currentTokens = counter.EstimateTokens(currentText);
      }

      sw.Stop();
      return CompressedContextFactory.Compressed(text, currentText, originalTokens, currentTokens, StrategyName, sw.Elapsed);
   }

   /// <inheritdoc />
   public async Task<CompressedContext> CompressBatchAsync(IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(texts);
      var merged = string.Join("\n\n", texts);
      return await CompressAsync(merged, options, ct);
   }
}
