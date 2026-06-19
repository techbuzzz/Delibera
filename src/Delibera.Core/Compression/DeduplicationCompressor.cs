using System.Diagnostics;

namespace Delibera.Core.Compression;

/// <summary>
///    Deduplication compression strategy — identifies and removes semantically similar
///    or duplicate sentences/paragraphs from the context.
/// </summary>
/// <remarks>
///    <para>
///       Particularly effective when multiple debate participants repeat the same points.
///       When an <see cref="IEmbeddingProvider" /> is available, uses cosine similarity;
///       otherwise falls back to normalized Levenshtein distance heuristics.
///    </para>
/// </remarks>
public sealed class DeduplicationCompressor(IEmbeddingProvider? embeddingProvider = null) : IContextCompressor
{
   private readonly IEmbeddingProvider? _embeddingProvider = embeddingProvider;

   /// <inheritdoc />
   public string StrategyName => "Deduplication";

   /// <inheritdoc />
   public string Description => "Removes semantically similar or duplicate content.";

   /// <inheritdoc />
   public async Task<CompressedContext> CompressAsync(string text, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var sw = Stopwatch.StartNew();
      options ??= CompressionOptions.Default;
      var counter = TokenCounter.Default;
      var originalTokens = counter.EstimateTokens(text);

      var sentences = SemanticCompressor.SplitSentences(text);
      if (sentences.Count <= 1)
         return CompressedContextFactory.PassThrough(text, originalTokens, StrategyName, sw.Elapsed);

      var unique = _embeddingProvider is not null
         ? await DeduplicateWithEmbeddingsAsync(sentences, options.DeduplicationThreshold, ct)
         : DeduplicateWithHeuristics(sentences, options.DeduplicationThreshold);

      var compressedText = string.Join(" ", unique);
      var compressedTokens = counter.EstimateTokens(compressedText);

      sw.Stop();
      return CompressedContextFactory.Compressed(text, compressedText, originalTokens, compressedTokens, StrategyName, sw.Elapsed);
   }

   /// <inheritdoc />
   public async Task<CompressedContext> CompressBatchAsync(IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(texts);
      var merged = string.Join("\n\n", texts);
      return await CompressAsync(merged, options, ct);
   }

   // ──────────────────────────────────────────────

   private async Task<List<string>> DeduplicateWithEmbeddingsAsync(
      List<SemanticCompressor.SentenceSpan> sentences,
      double threshold,
      CancellationToken ct)
   {
      var texts = sentences.Select(s => s.Text).ToList();
      var vectors = await _embeddingProvider!.EmbedBatchAsync(texts, ct);

      var kept = new List<string>();
      var keptVectors = new List<float[]>(sentences.Count);

      for (var i = 0; i < sentences.Count; i++)
      {
         var isDuplicate = false;
         foreach (var kv in keptVectors)
         {
            var sim = SemanticCompressor.CosineSimilarity(vectors[i], kv);
            if (sim >= threshold)
            {
               isDuplicate = true;
               break;
            }
         }

         if (!isDuplicate)
         {
            kept.Add(sentences[i].Text);
            keptVectors.Add(vectors[i]);
         }
      }

      return kept;
   }

   private static List<string> DeduplicateWithHeuristics(
      List<SemanticCompressor.SentenceSpan> sentences,
      double threshold)
   {
      var kept = new List<string>();
      var keptNormalized = new List<string>();

      foreach (var s in sentences)
      {
         var normalized = NormalizeText(s.Text);
         var isDuplicate = false;

         foreach (var k in keptNormalized)
         {
            var similarity = ComputeTextSimilarity(normalized, k);
            if (similarity >= threshold)
            {
               isDuplicate = true;
               break;
            }
         }

         if (!isDuplicate)
         {
            kept.Add(s.Text);
            keptNormalized.Add(normalized);
         }
      }

      return kept;
   }

   /// <summary>
   ///    Computes a rough text similarity based on shared word overlap (Jaccard-like).
   /// </summary>
   private static double ComputeTextSimilarity(string a, string b)
   {
      var wordsA = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
      var wordsB = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

      if (wordsA.Count == 0 || wordsB.Count == 0) return 0;

      var intersection = wordsA.Intersect(wordsB, StringComparer.OrdinalIgnoreCase).Count();
      var union = wordsA.Union(wordsB, StringComparer.OrdinalIgnoreCase).Count();

      return union > 0
         ? (double)intersection / union
         : 0;
   }

   private static string NormalizeText(string text)
   {
      return text.Trim().ToLowerInvariant();
   }
}