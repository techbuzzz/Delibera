using System.Diagnostics;

namespace Delibera.Core.Compression;

/// <summary>
/// Semantic compression strategy — embeds sentences, ranks them by relevance
/// to the overall topic, and keeps the most important ones up to the target ratio.
/// </summary>
/// <remarks>
/// <para>Algorithm:</para>
/// <list type="number">
///   <item>Split text into sentences.</item>
///   <item>Compute embeddings for each sentence and for the full text (topic vector).</item>
///   <item>Score each sentence by cosine similarity to the topic vector.</item>
///   <item>Keep top-N sentences (preserving original order) to meet the target ratio.</item>
/// </list>
/// </remarks>
public sealed class SemanticCompressor : IContextCompressor
{
   private readonly IEmbeddingProvider _embeddingProvider;

   /// <inheritdoc/>
   public string StrategyName => "Semantic";

   /// <inheritdoc/>
   public string Description => "Ranks sentences by semantic relevance and keeps the most important ones.";

   /// <summary>
   /// Creates a semantic compressor.
   /// </summary>
   /// <param name="embeddingProvider">Embedding provider for computing sentence vectors.</param>
   public SemanticCompressor(IEmbeddingProvider embeddingProvider)
   {
      _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
   }

   /// <inheritdoc/>
   public async Task<CompressedContext> CompressAsync(string text, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var sw = Stopwatch.StartNew();
      options ??= CompressionOptions.Default;
      var counter = TokenCounter.Default;
      var originalTokens = counter.EstimateTokens(text);

      if (originalTokens == 0)
         return EmptyResult(text, options, sw.Elapsed);

      var sentences = SplitSentences(text);
      if (sentences.Count <= 2)
         return PassThrough(text, originalTokens, sw.Elapsed);

      // Compute topic embedding (centroid of entire text)
      var topicVector = await _embeddingProvider.EmbedAsync(text.Length > 2000 ? text[..2000] : text, ct);

      // Compute sentence embeddings
      var sentenceTexts = sentences.Select(s => s.Text).ToList();
      var sentenceVectors = await _embeddingProvider.EmbedBatchAsync(sentenceTexts, ct);

      // Score each sentence by similarity to topic
      var scored = new List<(int Index, double Score, string Text)>();
      for (var i = 0; i < sentences.Count; i++)
      {
         var sim = CosineSimilarity(sentenceVectors[i], topicVector);
         scored.Add((i, sim, sentences[i].Text));
      }

      // Determine how many sentences to keep
      var targetTokens = options.MaxOutputTokens ?? (int)(originalTokens * options.TargetRatio);
      var kept = SelectTopSentences(scored, targetTokens, counter, options);

      var compressedText = string.Join(" ", kept.OrderBy(k => k.Index).Select(k => k.Text));
      var compressedTokens = counter.EstimateTokens(compressedText);

      sw.Stop();
      return new CompressedContext
      {
         Text = compressedText,
         OriginalLength = text.Length,
         CompressedLength = compressedText.Length,
         OriginalTokens = originalTokens,
         CompressedTokens = compressedTokens,
         StrategyUsed = StrategyName,
         Duration = sw.Elapsed
      };
   }

   /// <inheritdoc/>
   public async Task<CompressedContext> CompressBatchAsync(IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default)
   {
      var merged = string.Join("\n\n", texts);
      return await CompressAsync(merged, options, ct);
   }

   // ──────────────────────────────────────────────
   // Helpers
   // ──────────────────────────────────────────────

   private static List<(int Index, double Score, string Text)> SelectTopSentences(
       List<(int Index, double Score, string Text)> scored,
       int targetTokens,
       TokenCounter counter,
       CompressionOptions options)
   {
      var sorted = scored.OrderByDescending(s => s.Score).ToList();
      var selected = new List<(int Index, double Score, string Text)>();
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
      var sentences = new List<SentenceSpan>();
      var delimiters = new[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" };
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

   internal static double CosineSimilarity(float[] a, float[] b)
   {
      if (a.Length != b.Length) return 0;
      double dot = 0, magA = 0, magB = 0;
      for (var i = 0; i < a.Length; i++)
      {
         dot += a[i] * b[i];
         magA += a[i] * a[i];
         magB += b[i] * b[i];
      }
      var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
      return denom > 0 ? dot / denom : 0;
   }

   private static CompressedContext PassThrough(string text, int tokens, TimeSpan duration) => new()
   {
      Text = text,
      OriginalLength = text.Length,
      CompressedLength = text.Length,
      OriginalTokens = tokens,
      CompressedTokens = tokens,
      StrategyUsed = "Semantic",
      Duration = duration
   };

   private static CompressedContext EmptyResult(string text, CompressionOptions options, TimeSpan duration) => new()
   {
      Text = text,
      OriginalLength = text.Length,
      CompressedLength = text.Length,
      OriginalTokens = 0,
      CompressedTokens = 0,
      StrategyUsed = "Semantic",
      Duration = duration
   };

   /// <summary>A sentence with its original position in the text.</summary>
   internal record struct SentenceSpan(int StartPos, string Text);
}
