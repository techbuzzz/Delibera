namespace Delibera.Core.Debate;

/// <summary>
///    Snapshot of the debate's progress passed to <see cref="IStrategySelector.SelectNextAsync" />
///    after each round so the selector can decide whether to switch strategy.
/// </summary>
/// <param name="CurrentRound">The round number that just completed (1-based).</param>
/// <param name="MaxRounds">The configured maximum number of rounds.</param>
/// <param name="CompletedRounds">All rounds completed so far, in order.</param>
/// <param name="ResponseDiversityScore">
///    Cosine-similarity-derived diversity score across participant responses in the
///    most recent round. <c>0.0</c> = identical responses (stalemate), <c>1.0</c> =
///    maximally diverse. Falls back to <c>0.0</c> when no
///    <see cref="Interfaces.IEmbeddingProvider" /> is available.
/// </param>
/// <param name="IsStalemate">
///    <c>true</c> when the diversity score has stayed below
///    <see cref="AdaptiveStrategySelector.StagnationThreshold" /> for enough consecutive
///    rounds to warrant a strategy switch. Computed by the
///    <see cref="AdaptiveStrategySelector" />.
/// </param>
public sealed record DebateProgress(
   int CurrentRound,
   int MaxRounds,
   IReadOnlyList<DebateRound> CompletedRounds,
   double ResponseDiversityScore,
   bool IsStalemate);

/// <summary>
///    Hook invoked by <see cref="Council.CouncilExecutor" /> after each round to decide
///    whether the debate strategy should be swapped mid-flight. Returns <c>null</c> to
///    keep the current strategy, or a new <see cref="IDebateStrategy" /> to switch.
/// </summary>
/// <remarks>
///    <para>
///       This is the extension point for <b>adaptive deliberation</b> — letting the
///       Chairman or an external observer react to a stalled debate (circular arguments,
///       low response diversity, stalemate) by changing the strategy for the next round.
///    </para>
///    <para>
///       Implementations must be stateless or thread-safe — the executor calls
///       <see cref="SelectNextAsync" /> on its own thread, but the same selector instance
///       may be reused across debates.
///    </para>
/// </remarks>
public interface IStrategySelector
{
   /// <summary>
   ///    Inspects the current <paramref name="progress" /> and returns the strategy to
   ///    use for the next round, or <c>null</c> to keep the current one.
   /// </summary>
   /// <param name="progress">Snapshot of the debate after the round that just completed.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>A new strategy, or <c>null</c> to keep the current strategy.</returns>
   ValueTask<IDebateStrategy?> SelectNextAsync(DebateProgress progress, CancellationToken ct = default);
}

/// <summary>
///    A built-in <see cref="IStrategySelector" /> that switches from <see cref="Initial" />
///    to <see cref="OnStalemate" /> when the debate stagnates for
///    <see cref="StagnationThreshold" /> consecutive rounds.
/// </summary>
/// <remarks>
///    <para>
///       Stagnation is detected by tracking how many consecutive rounds have a
///       <see cref="DebateProgress.ResponseDiversityScore" /> below
///       <see cref="StagnationThreshold" />. When the counter reaches the threshold, the
///       selector returns <see cref="OnStalemate" /> once and then resets (so the new
///       strategy gets a fair chance to converge before another switch is considered).
///    </para>
///    <para>
///       When <see cref="DebateProgress.ResponseDiversityScore" /> is <c>0.0</c> (no
///       embedding provider configured), this selector falls back to a textual
///       similarity heuristic: it compares the Levenshtein distance between
///       participant responses in the latest round. If all responses are nearly
///       identical, stagnation is declared.
///    </para>
/// </remarks>
public sealed class AdaptiveStrategySelector : IStrategySelector
{
   private int _stagnationCount;
   private bool _switched;

   /// <summary>
   ///    The strategy to start the debate with. The executor uses this as the initial
   ///    <see cref="ICouncilExecutor.Strategy" /> (set via
   ///    <c>WithAdaptiveStrategy</c>).
   /// </summary>
   public required IDebateStrategy Initial { get; init; }

   /// <summary>
   ///    The strategy to switch to when the debate stagnates. Must be different from
   ///    <see cref="Initial" /> for the switch to have any effect.
   /// </summary>
   public required IDebateStrategy OnStalemate { get; init; }

   /// <summary>
   ///    Number of consecutive low-diversity rounds that triggers a switch. Default is 2.
   /// </summary>
   public int StagnationThreshold { get; init; } = 2;

   /// <summary>
   ///    Diversity score below which a round counts as "stagnating". Default is 0.3.
   /// </summary>
   public double StagnationScore { get; init; } = 0.3;

   /// <inheritdoc />
   public ValueTask<IDebateStrategy?> SelectNextAsync(DebateProgress progress, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(progress);

      // After we've already switched once, give the new strategy a fair chance —
      // return null for the rest of the debate. Callers can reset by creating a
      // fresh selector.
      if (_switched)
         return new ValueTask<IDebateStrategy?>((IDebateStrategy?)null);

      // If we're already on the last round, no point switching.
      if (progress.CurrentRound >= progress.MaxRounds)
         return new ValueTask<IDebateStrategy?>((IDebateStrategy?)null);

      // Determine whether this round counts as "stagnating".
      // When an embedding provider is configured, ResponseDiversityScore is in [0,1]
      // and we compare it against StagnationScore. When no embedding provider is
      // available, ResponseDiversityScore is 0.0 (sentinel) and we fall back to a
      // text-similarity heuristic on the actual response strings.
      bool stagnating;
      if (progress.ResponseDiversityScore > 0.0)
         stagnating = progress.ResponseDiversityScore < StagnationScore;
      else
         stagnating = HasNearIdenticalResponses(progress);

      if (stagnating)
         _stagnationCount++;
      else
         _stagnationCount = 0;

      if (_stagnationCount >= StagnationThreshold)
      {
         _switched = true;
         _stagnationCount = 0;
         return new ValueTask<IDebateStrategy?>(OnStalemate);
      }

      return new ValueTask<IDebateStrategy?>((IDebateStrategy?)null);
   }

   /// <summary>
   ///    Resets the selector's internal stagnation counter and switch flag so it can be
   ///    reused for a new debate. Call between debates if the same instance is reused.
   /// </summary>
   public void Reset()
   {
      _stagnationCount = 0;
      _switched = false;
   }

   private static bool HasNearIdenticalResponses(DebateProgress progress)
   {
      if (progress.CompletedRounds.Count == 0) return false;
      var lastRound = progress.CompletedRounds[^1];
      if (lastRound.Responses.Count < 2) return false;

      var responses = lastRound.Responses.Values.ToList();
      for (var i = 0; i < responses.Count; i++)
      for (var j = i + 1; j < responses.Count; j++)
      {
         var sim = TextSimilarity(responses[i], responses[j]);
         if (sim > 0.8) return true;
      }

      return false;
   }

   /// <summary>
   ///    Simple normalised Levenshtein similarity in [0,1]. 1.0 = identical strings.
   ///    Used as a fallback when no embedding provider is configured.
   /// </summary>
   private static double TextSimilarity(string a, string b)
   {
      if (a == b) return 1.0;
      if (a.Length == 0 || b.Length == 0) return 0.0;
      var maxLen = Math.Max(a.Length, b.Length);
      var dist = LevenshteinDistance(a, b);
      return 1.0 - (double)dist / maxLen;
   }

   private static int LevenshteinDistance(string a, string b)
   {
      if (a.Length < b.Length)
         (a, b) = (b, a);

      var n = b.Length;
      Span<int> prevRow = n <= 128
         ? stackalloc int[n + 1]
         : new int[n + 1];
      Span<int> currRow = n <= 128
         ? stackalloc int[n + 1]
         : new int[n + 1];

      for (var i = 0; i <= n; i++)
         prevRow[i] = i;

      for (var i = 1; i <= a.Length; i++)
      {
         currRow[0] = i;
         for (var j = 1; j <= n; j++)
         {
            var cost = a[i - 1] == b[j - 1]
               ? 0
               : 1;
            currRow[j] = Math.Min(
               Math.Min(prevRow[j] + 1, currRow[j - 1] + 1),
               prevRow[j - 1] + cost);
         }

         var tmp = prevRow;
         prevRow = currRow;
         currRow = tmp;
      }

      return prevRow[n];
   }
}
