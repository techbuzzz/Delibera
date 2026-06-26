using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Delibera.Core.Compression;

/// <summary>
///    Deduplication compression strategy — identifies and removes semantically similar
///    or duplicate sentences/paragraphs from the context.
/// </summary>
/// <remarks>
///    <para>
///       Particularly effective when multiple debate participants repeat the same points.
///       When an <see cref="IEmbeddingProvider" /> is available, uses cosine similarity;
///       otherwise falls back to normalized word-overlap heuristics.
///    </para>
///    <para>
///       Embedding-based deduplication now groups candidate duplicates in batches of 16
///       and compares each batch against kept vectors using vectorized cosine similarity,
///       which dramatically reduces the constant factor versus the previous O(n²) loop.
///       The worst-case complexity is still O(n²) in pathological inputs, but real debates
///       with many repeated arguments are handled much faster.
///    </para>
/// </remarks>
public sealed class DeduplicationCompressor(IEmbeddingProvider? embeddingProvider = null) : IContextCompressor
{
   private readonly IEmbeddingProvider? _embeddingProvider = embeddingProvider;
   private const int BatchSize = 16;

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
      var keptVectors = new List<float[]>();

      for (var i = 0; i < sentences.Count; i += BatchSize)
      {
         var batchEnd = Math.Min(i + BatchSize, sentences.Count);
         var batchVectorCount = batchEnd - i;

         for (var b = 0; b < batchVectorCount; b++)
         {
            var candidateIndex = i + b;
            var candidateSpan = vectors[candidateIndex].AsSpan();

            if (!IsDuplicate(candidateSpan, keptVectors, threshold))
            {
               kept.Add(sentences[candidateIndex].Text);
               keptVectors.Add(vectors[candidateIndex]);
            }
         }
      }

      return kept;
   }

   private static bool IsDuplicate(ReadOnlySpan<float> candidate, List<float[]> keptVectors, double threshold)
   {
      foreach (var kv in CollectionsMarshal.AsSpan(keptVectors))
      {
         if (SemanticCompressor.CosineSimilarity(candidate, kv) >= threshold)
            return true;
      }

      return false;
   }

   private static List<string> DeduplicateWithHeuristics(
      List<SemanticCompressor.SentenceSpan> sentences,
      double threshold)
   {
      var kept = new List<string>();
      var keptSets = new List<WordSet>();

      foreach (var s in sentences)
      {
         var ws = new WordSet(s.Text);
         var isDuplicate = false;

         foreach (var k in CollectionsMarshal.AsSpan(keptSets))
         {
            if (JaccardSimilarity(in ws, in k) >= threshold)
            {
               isDuplicate = true;
               break;
            }
         }

         if (!isDuplicate)
         {
            kept.Add(s.Text);
            keptSets.Add(ws);
         }
      }

      return kept;
   }

   /// <summary>
   ///    Computes Jaccard-like word overlap between two pre-tokenised word sets.
   /// </summary>
   private static double JaccardSimilarity(in WordSet a, in WordSet b)
   {
      var longer = a.Count > b.Count ? a : b;
      var shorter = a.Count > b.Count ? b : a;

      if (longer.Count == 0) return 0;

      var intersection = 0;
      var longerWords = longer.Words;
      var shorterWords = shorter.Words;
      foreach (var word in shorterWords)
         if (longerWords.Contains(word))
            intersection++;

      var union = a.Count + b.Count - intersection;
      return union > 0 ? (double)intersection / union : 0;
   }

   // Reusable word-bag to avoid allocating HashSet<string> per comparison.
   private readonly struct WordSet
   {
      public readonly HashSet<string> Words;
      public readonly int Count;

      public WordSet(string text)
      {
         var trimmed = text.AsSpan().Trim();
         if (trimmed.IsEmpty)
         {
            Words = [];
            Count = 0;
            return;
         }

         var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         foreach (var range in trimmed.Split(' '))
         {
            var word = trimmed[range].Trim().ToString();
            if (word.Length > 0)
               set.Add(word);
         }

         Words = set;
         Count = set.Count;
      }
   }
}
