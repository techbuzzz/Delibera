using System.Collections.Concurrent;

namespace Delibera.Core.Compression;

/// <summary>
///    Estimates token counts for text using a heuristic word/character-based approximation.
///    Provides a fast, dependency-free alternative to model-specific tokenisers.
/// </summary>
/// <remarks>
///    <para>
///       The default heuristic uses the "4 characters ≈ 1 token" rule common for GPT-style models.
///       For Llama-family models, the ratio is closer to 3.5 characters per token.
///    </para>
///    <para>For precise counts, provide a custom <see cref="TokenizerFunc" />.</para>
///    <para>
///       The default instance memoizes short (≤ 8 000 character) string estimates in a small
///       LRU cache to avoid recomputing the heuristic on the same prompt fragments, which are
///       frequently reused across debate rounds.
///    </para>
/// </remarks>
public sealed class TokenCounter
{
   private static readonly Lazy<TokenCounter> DefaultInstance = new(
      () => new TokenCounter(),
      LazyThreadSafetyMode.ExecutionAndPublication);

   /// <summary>Gets the shared default <see cref="TokenCounter" /> instance.</summary>
   public static TokenCounter Default => DefaultInstance.Value;

   /// <summary>
   ///    Custom tokenizer function. If set, overrides the heuristic estimator.
   ///    Takes a string and returns its token count.
   /// </summary>
   public Func<string, int>? TokenizerFunc { get; init; }

   /// <summary>
   ///    Characters-per-token ratio for the heuristic estimator.
   ///    Default is 4.0 (GPT-style). Set to 3.5 for Llama-family models.
   /// </summary>
   public double CharsPerToken { get; init; } = 4.0;

   /// <summary>
   ///    Maximum length of strings that will be memoized by the default instance.
   ///    Longer strings bypass the cache because cache lookups can cost more than the estimate.
   ///    Default is 8 000 characters.
   /// </summary>
   public int MaxMemoizedLength { get; init; } = 8000;

   /// <summary>
   ///    Maximum number of memoized estimates retained by the default instance.
   ///    Default is 1 024 entries. Set to 0 to disable memoization.
   /// </summary>
   public int MaxMemoizedEntries { get; init; } = 1024;

   /// <summary>
   ///    Estimates the token count for the given text.
   /// </summary>
   /// <param name="text">Input text.</param>
   /// <returns>Estimated token count.</returns>
   public int EstimateTokens(string? text)
   {
      if (string.IsNullOrEmpty(text)) return 0;

      if (TokenizerFunc is not null)
         return TokenizerFunc(text);

      if (text.Length <= MaxMemoizedLength && MaxMemoizedEntries > 0)
      {
         if (_memo.TryGetValue(text, out var cached))
         {
            // Touch: move to end of access-order list for LRU.
            TouchMemoEntry(text);
            return cached;
         }

         var value = EstimateTokens(text.AsSpan());

         // Evict oldest entries if at capacity before adding.
         if (_memo.Count >= MaxMemoizedEntries)
            EvictMemoEntries(_evictionBatchSize);

         if (_memo.TryAdd(text, value))
            TrackMemoEntry(text);

         return value;
      }

      return EstimateTokens(text.AsSpan());
   }

   /// <summary>
   ///    Estimates the token count for the given text span without allocating.
   ///    Note: a custom <see cref="TokenizerFunc" /> is ignored on this allocation-free path
   ///    and memoization is not available for spans.
   /// </summary>
   /// <param name="text">Input text span.</param>
   /// <returns>Estimated token count.</returns>
   public int EstimateTokens(ReadOnlySpan<char> text)
   {
      if (text.IsEmpty) return 0;

      // Heuristic: count words + account for sub-word tokenization
      // Blend word-count and char-count estimates for better accuracy
      var wordCount = CountWords(text);
      var charEstimate = (int)Math.Ceiling(text.Length / CharsPerToken);

      // Weighted average — word count is generally more accurate for English,
      // but char count handles code and non-Latin scripts better
      return (int)Math.Ceiling(wordCount * 0.6 + charEstimate * 0.4);
   }

   /// <summary>
   ///    Estimates token count for multiple texts and returns the total.
   /// </summary>
   public int EstimateTokens(IEnumerable<string> texts)
   {
      ArgumentNullException.ThrowIfNull(texts);
      var total = 0;
      foreach (var t in texts)
         total += EstimateTokens(t);
      return total;
   }

   /// <summary>
   ///    Returns <c>true</c> if the text exceeds the specified token limit.
   /// </summary>
   public bool ExceedsLimit(string text, int tokenLimit)
   {
      return EstimateTokens(text) > tokenLimit;
   }

   /// <summary>
   ///    Truncates text to approximately fit within the specified token limit.
   /// </summary>
   /// <param name="text">Input text.</param>
   /// <param name="maxTokens">Maximum token count.</param>
   /// <returns>Truncated text (may be the original if already within limit).</returns>
   public string TruncateToTokenLimit(string text, int maxTokens)
   {
      ArgumentNullException.ThrowIfNull(text);
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

   private readonly ConcurrentDictionary<string, int> _memo = new();
   private readonly LinkedList<string> _memoOrder = new(); // LRU access order
   private readonly object _memoLock = new();
   private const int _evictionBatchSize = 64; // evict 64 entries at a time when over capacity

   private void TrackMemoEntry(string key)
   {
      lock (_memoLock)
      {
         _memoOrder.AddLast(key);
      }
   }

   private void TouchMemoEntry(string key)
   {
      lock (_memoLock)
      {
         // Remove and re-add to move to end (most recently used).
         _memoOrder.Remove(key);
         _memoOrder.AddLast(key);
      }
   }

   private void EvictMemoEntries(int count)
   {
      lock (_memoLock)
      {
         var removed = 0;
         var node = _memoOrder.First;
         while (node is not null && removed < count)
         {
            var next = node.Next;
            _memo.TryRemove(node.Value, out _);
            _memoOrder.Remove(node);
            node = next;
            removed++;
         }
      }
   }

   private static int CountWords(ReadOnlySpan<char> text)
   {
      var count = 0;
      var inWord = false;
      foreach (var c in text)
         if (char.IsWhiteSpace(c))
         {
            inWord = false;
         }
         else if (!inWord)
         {
            inWord = true;
            count++;
         }

      return count;
   }
}