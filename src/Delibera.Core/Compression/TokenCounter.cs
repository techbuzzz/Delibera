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
///       The default instance memoizes short (≤ 8 000 character) string estimates to avoid
///       CLOCK (second-chance) cache — a hit only sets a per-entry flag, so the read path
///       recomputing the heuristic on the same prompt fragments, which are frequently reused
///       across debate rounds. The previous implementation moved a linked-list node on every
///       hit under an exclusive lock, which serialised every concurrently-estimated prompt in
///       the process: <see cref="Default" /> is a process-wide singleton, and debate
///       participants are fanned out with Task.WhenAll.
///    </para>
/// </remarks>
public sealed class TokenCounter
{
   private const int _evictionBatchSize = 64; // evict 64 entries at a time when over capacity

   private static readonly Lazy<TokenCounter> DefaultInstance = new(
      () => new TokenCounter(),
      LazyThreadSafetyMode.ExecutionAndPublication);

   // ──────────────────────────────────────────────

   private readonly ConcurrentDictionary<string, MemoEntry> _memo = new();
   private ConcurrentQueue<string> _memoOrder = new(); // insertion order, re-queued on use

   /// <summary>Gets the shared default <see cref="TokenCounter" /> instance.</summary>
   public static TokenCounter Default => DefaultInstance.Value;

   // ── Test-only observation of the cache invariants ───────────────────────────
   //
   // MaxMemoizedEntries and the queue's ability to shrink are documented behaviour, so
   // the tests need to see them. Kept internal so the public surface stays unchanged.

   /// <summary>Number of currently memoized estimates.</summary>
   internal int MemoizedCount => _memo.Count;

   /// <summary>Number of keys queued for eviction.</summary>
   internal int EvictionQueueLength => _memoOrder.Count;

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
            // Second chance: a plain flag write, no lock and no list surgery. This is the
            // hot path — it runs once per prompt per participant per round.
            cached.Used = true;
            return cached.Value;
         }

         var value = EstimateTokens(text.AsSpan());

         // Evict oldest entries if at capacity before adding.
         if (_memo.Count >= MaxMemoizedEntries)
            EvictMemoEntries(_evictionBatchSize);

         if (_memo.TryAdd(text, new MemoEntry(value)))
         {
            _memoOrder.Enqueue(text);
            TrimMemoOrder();
         }

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

      // Concat over the trimmed span instead of slicing then concatenating: the slice and
      // the TrimEnd each allocated a throwaway string before this.
      return string.Concat(text.AsSpan(0, cutoff).TrimEnd(), "…");
   }

   /// <summary>
   ///    Evicts up to <paramref name="count" /> entries, giving a second chance to any that
   ///    were used since the eviction queue last passed them. Lock-free: the queue is a
   ///    <see cref="ConcurrentQueue{T}" /> and the per-entry flag is racy by design.
   /// </summary>
   private void EvictMemoEntries(int count)
   {
      var removed = 0;
      var scanned = 0;

      // Bound the scan: a cache full of hot keys must not spin here forever. Stopping
      // early only means the next pass will finish the job.
      var budget = Math.Max(count * 8, _memoOrder.Count);

      while (removed < count && scanned < budget && _memoOrder.TryDequeue(out var key))
      {
         scanned++;

         if (_memo.TryGetValue(key, out var entry) && entry.Used)
         {
            entry.Used = false; // second chance: back of the queue, no longer "recently used"
            _memoOrder.Enqueue(key);
            continue;
         }

         // Only count a real removal, so a stale queue entry cannot consume the budget.
         if (_memo.TryRemove(key, out _))
            removed++;
      }
   }

   /// <summary>
   ///    Keeps the eviction queue from outgrowing the cache. A second-chance re-queue adds
   ///    an item without removing one, so a workload of uniformly hot keys would otherwise
   ///    lengthen the queue indefinitely. The rebuild keeps every cached key, so nothing
   ///    can become permanently un-evictable; if another thread swapped the queue first,
   ///    the attempt is simply discarded and the next one retries.
   /// </summary>
   private void TrimMemoOrder()
   {
      var order = _memoOrder;
      if (order.Count <= 2 * MaxMemoizedEntries) return;

      var present = new HashSet<string>(order, StringComparer.Ordinal);
      var rebuilt = new ConcurrentQueue<string>(order);
      foreach (var key in _memo.Keys)
         if (present.Add(key))
            rebuilt.Enqueue(key);

      Interlocked.CompareExchange(ref _memoOrder, rebuilt, order);
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

   /// <summary>A memoized estimate plus its second-chance flag.</summary>
   private sealed class MemoEntry(int value)
   {
      /// <summary>
      ///    Second-chance flag, set on every cache hit. Deliberately written without a
      ///    lock: a lost or duplicated write only makes the eviction estimate slightly
      ///    less precise, which for a token-count heuristic is not observable.
      /// </summary>
      internal bool Used;

      internal int Value { get; } = value;
   }
}
