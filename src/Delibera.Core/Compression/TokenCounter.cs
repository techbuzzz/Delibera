namespace Delibera.Core.Compression;

/// <summary>
/// Estimates token counts for text using a heuristic word/character-based approximation.
/// Provides a fast, dependency-free alternative to model-specific tokenisers.
/// </summary>
/// <remarks>
/// <para>The default heuristic uses the "4 characters ≈ 1 token" rule common for GPT-style models.
/// For Llama-family models, the ratio is closer to 3.5 characters per token.</para>
/// <para>For precise counts, provide a custom <see cref="TokenizerFunc"/>.</para>
/// </remarks>
public sealed class TokenCounter
{
   private static readonly Lazy<TokenCounter> DefaultInstance = new(() => new TokenCounter());

   /// <summary>Gets the shared default <see cref="TokenCounter"/> instance.</summary>
   public static TokenCounter Default => DefaultInstance.Value;

   /// <summary>
   /// Custom tokenizer function. If set, overrides the heuristic estimator.
   /// Takes a string and returns its token count.
   /// </summary>
   public Func<string, int>? TokenizerFunc { get; init; }

   /// <summary>
   /// Characters-per-token ratio for the heuristic estimator.
   /// Default is 4.0 (GPT-style). Set to 3.5 for Llama-family models.
   /// </summary>
   public double CharsPerToken { get; init; } = 4.0;

   /// <summary>
   /// Estimates the token count for the given text.
   /// </summary>
   /// <param name="text">Input text.</param>
   /// <returns>Estimated token count.</returns>
   public int EstimateTokens(string? text)
   {
      if (string.IsNullOrEmpty(text)) return 0;

      if (TokenizerFunc is not null)
         return TokenizerFunc(text);

      // Heuristic: count words + account for sub-word tokenization
      // Blend word-count and char-count estimates for better accuracy
      var wordCount = CountWords(text);
      var charEstimate = (int)Math.Ceiling(text.Length / CharsPerToken);

      // Weighted average — word count is generally more accurate for English,
      // but char count handles code and non-Latin scripts better
      return (int)Math.Ceiling(wordCount * 0.6 + charEstimate * 0.4);
   }

   /// <summary>
   /// Estimates token count for multiple texts and returns the total.
   /// </summary>
   public int EstimateTokens(IEnumerable<string> texts)
   {
      var total = 0;
      foreach (var t in texts)
         total += EstimateTokens(t);
      return total;
   }

   /// <summary>
   /// Returns <c>true</c> if the text exceeds the specified token limit.
   /// </summary>
   public bool ExceedsLimit(string text, int tokenLimit) =>
       EstimateTokens(text) > tokenLimit;

   /// <summary>
   /// Truncates text to approximately fit within the specified token limit.
   /// </summary>
   /// <param name="text">Input text.</param>
   /// <param name="maxTokens">Maximum token count.</param>
   /// <returns>Truncated text (may be the original if already within limit).</returns>
   public string TruncateToTokenLimit(string text, int maxTokens)
   {
      if (string.IsNullOrEmpty(text)) return string.Empty;
      if (EstimateTokens(text) <= maxTokens) return text;

      // Approximate character position for the token limit
      var approxChars = (int)(maxTokens * CharsPerToken);
      if (approxChars >= text.Length) return text;

      // Find a sentence boundary near the target
      var cutoff = text.LastIndexOf(". ", approxChars, StringComparison.Ordinal);
      if (cutoff < approxChars / 2) cutoff = approxChars; // no good boundary

      return text[..cutoff].TrimEnd() + "…";
   }

   // ──────────────────────────────────────────────

   private static int CountWords(string text)
   {
      var count = 0;
      var inWord = false;
      foreach (var c in text)
      {
         if (char.IsWhiteSpace(c))
         {
            inWord = false;
         }
         else if (!inWord)
         {
            inWord = true;
            count++;
         }
      }
      return count;
   }
}
