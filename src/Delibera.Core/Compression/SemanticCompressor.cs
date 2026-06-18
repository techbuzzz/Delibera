using System.Diagnostics;
using System.Numerics;

namespace Delibera.Core.Compression;

/// <summary>
///    Semantic compression strategy — embeds sentences, ranks them by relevance
///    to the overall topic, and keeps the most important ones up to the target ratio.
/// </summary>
/// <remarks>
///    <para>Algorithm:</para>
///    <list type="number">
///       <item>Split text into sentences.</item>
///       <item>Compute embeddings for each sentence and for the full text (topic vector).</item>
///       <item>Score each sentence by cosine similarity to the topic vector.</item>
///       <item>Keep top-N sentences (preserving original order) to meet the target ratio.</item>
///    </list>
/// </remarks>
public sealed class SemanticCompressor(IEmbeddingProvider embeddingProvider) : IContextCompressor
{
   private readonly IEmbeddingProvider _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));

   /// <inheritdoc />
   public string StrategyName => "Semantic";

   /// <inheritdoc />
   public string Description => "Ranks sentences by semantic relevance and keeps the most important ones.";

   /// <inheritdoc />
   public async Task<CompressedContext> CompressAsync(string text, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var sw = Stopwatch.StartNew();
      options ??= CompressionOptions.Default;
      var counter = TokenCounter.Default;
      var originalTokens = counter.EstimateTokens(text);

      if (originalTokens == 0)
         return CompressedContextFactory.PassThrough(text, 0, StrategyName, sw.Elapsed);

      var sentences = SplitSentences(text);
      if (sentences.Count <= 2)
         return CompressedContextFactory.PassThrough(text, originalTokens, StrategyName, sw.Elapsed);

      // Compute topic embedding (centroid of entire text)
      var topicVector = await _embeddingProvider.EmbedAsync(text.Length > 2000
         ? text[..2000]
         : text, ct);

      // Compute sentence embeddings
      var sentenceTexts = sentences.Select(s => s.Text).ToList();
      var sentenceVectors = await _embeddingProvider.EmbedBatchAsync(sentenceTexts, ct);

      // Score each sentence by similarity to topic
      var scored = new List<ScoredSentence>(sentences.Count);
      for (var i = 0; i < sentences.Count; i++)
      {
         var sim = CosineSimilarity(sentenceVectors[i], topicVector);
         scored.Add(new ScoredSentence(i, sim, sentences[i].Text));
      }

      // Determine how many sentences to keep
      var targetTokens = options.MaxOutputTokens ?? (int)(originalTokens * options.TargetRatio);
      var kept = SelectTopSentences(scored, targetTokens, counter, options);

      var compressedText = string.Join(" ", kept.OrderBy(k => k.Index).Select(k => k.Text));
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
   // Helpers
   // ──────────────────────────────────────────────

   private static List<ScoredSentence> SelectTopSentences(
      List<ScoredSentence> scored,
      int targetTokens,
      TokenCounter counter,
      CompressionOptions options)
   {
      var sorted = scored.OrderByDescending(s => s.Score).ToList();
      var selected = new List<ScoredSentence>();
      var currentTokens = 0;

      foreach (var s in sorted)
      {
         // Preserve code blocks and structured content if requested
         if (options.PreserveCodeBlocks && (s.Text.Contains("```") || s.Text.TrimStart().StartsWith("    ")))
         {
            selected.Add(s);
            currentTokens += counter.EstimateTokens(s.Text);
            continue;
         }

         var sentenceTokens = counter.EstimateTokens(s.Text);
         if (currentTokens + sentenceTokens <= targetTokens)
         {
            selected.Add(s);
            currentTokens += sentenceTokens;
         }
      }

      return selected;
   }

   internal static List<SentenceSpan> SplitSentences(string text)
   {
      ArgumentNullException.ThrowIfNull(text);
      var sentences = new List<SentenceSpan>();
      ReadOnlySpan<string> delimiters = [". ", "! ", "? ", ".\n", "!\n", "?\n"];
      var pos = 0;

      while (pos < text.Length)
      {
         var nextDelim = int.MaxValue;
         var delimLen = 0;

         foreach (var d in delimiters)
         {
            var idx = text.IndexOf(d, pos, StringComparison.Ordinal);
            if (idx >= 0 && idx < nextDelim)
            {
               nextDelim = idx;
               delimLen = d.Length;
            }
         }

         if (nextDelim == int.MaxValue)
         {
            var remaining = text[pos..].Trim();
            if (remaining.Length > 0)
               sentences.Add(new SentenceSpan(pos, remaining));
            break;
         }

         var sentence = text[pos..(nextDelim + delimLen)].Trim();
         if (sentence.Length > 0)
            sentences.Add(new SentenceSpan(pos, sentence));

         pos = nextDelim + delimLen;
      }

      return sentences;
   }

   internal static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
   {
      if (a.Length != b.Length || a.IsEmpty) return 0;

      float dot = 0, magA = 0, magB = 0;
      var i = 0;

      // SIMD fast-path: process Vector<float>.Count lanes at a time.
      // On modern x64/ARM64 this is 8–16 floats per iteration.
      if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
      {
         var dotAcc = Vector<float>.Zero;
         var magAAcc = Vector<float>.Zero;
         var magBAcc = Vector<float>.Zero;
         var width = Vector<float>.Count;

         for (; i <= a.Length - width; i += width)
         {
            var va = new Vector<float>(a.Slice(i, width));
            var vb = new Vector<float>(b.Slice(i, width));
            dotAcc += va * vb;
            magAAcc += va * va;
            magBAcc += vb * vb;
         }

         dot = Vector.Dot(dotAcc, Vector<float>.One);
         magA = Vector.Dot(magAAcc, Vector<float>.One);
         magB = Vector.Dot(magBAcc, Vector<float>.One);
      }

      // Scalar tail (and full loop when SIMD is unavailable).
      for (; i < a.Length; i++)
      {
         dot += a[i] * b[i];
         magA += a[i] * a[i];
         magB += b[i] * b[i];
      }

      var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
      return denom > 0
         ? dot / denom
         : 0;
   }

   /// <summary>A sentence with its original position in the text.</summary>
   internal readonly record struct SentenceSpan(int StartPos, string Text);

   /// <summary>Sentence with computed relevance score.</summary>
   private readonly record struct ScoredSentence(int Index, double Score, string Text);
}