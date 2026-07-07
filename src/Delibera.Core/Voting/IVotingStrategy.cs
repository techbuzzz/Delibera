namespace Delibera.Core.Voting;

/// <summary>
///    A single ranked option on a participant's ballot.
/// </summary>
/// <param name="Name">Option name (e.g. a proposed answer, a candidate solution).</param>
/// <param name="Rank">Rank position — 1 is the top preference. Lower rank = preferred.</param>
public sealed record RankedOption(string Name, int Rank);

/// <summary>
///    A single participant's ballot in a council vote.
/// </summary>
/// <param name="MemberName">Council member display name.</param>
/// <param name="Weight">Vote weight. Must be non-negative. Default is 1.0.</param>
/// <param name="Rankings">Ranked options, ordered by preference (rank 1 = top).</param>
public sealed record ParticipantBallot(
   string MemberName,
   double Weight,
   IReadOnlyList<RankedOption> Rankings);

/// <summary>
///    The outcome of a council vote — winning option, full score table, and the
///    method used. Embedded into <see cref="Models.DebateResult.VotingTally" /> when a
///    voting chairman is configured.
/// </summary>
/// <param name="WinningOption">The option that won the vote.</param>
/// <param name="Score">The winning option's score (interpretation depends on the method).</param>
/// <param name="Scores">Full score table keyed by option name.</param>
/// <param name="Method">Voting method name (e.g. "Majority", "BordaCount", "Weighted").</param>
public sealed record VotingResult(
   string WinningOption,
   double Score,
   IReadOnlyDictionary<string, double> Scores,
   string Method);

/// <summary>
///    Strategy for tallying council votes. Implementations produce a
///    <see cref="VotingResult" /> from a set of <see cref="ParticipantBallot" />s.
/// </summary>
/// <remarks>
///    <para>
///       Used by <see cref="Council.Chairman.CreateVoting(string, ILLMProvider, IVotingStrategy)" />
///       to produce a verifiable, traceable decision trail as an alternative to the
///       single-LLM Chairman synthesis. Enterprise use cases (compliance, risk
///       committees, architecture boards) often require a tally rather than a synthesis.
///    </para>
///    <para>
///       Implementations must be stateless and thread-safe — the same instance may be
///       reused across debates.
///    </para>
/// </remarks>
public interface IVotingStrategy
{
   /// <summary>Unique method name (e.g. "Majority", "BordaCount", "Weighted").</summary>
   string MethodName { get; }

   /// <summary>
   ///    Tallies the ballots and produces a <see cref="VotingResult" />.
   /// </summary>
   /// <param name="ballots">Participant ballots.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The voting result with the winning option and full score table.</returns>
   Task<VotingResult> TallyAsync(IReadOnlyList<ParticipantBallot> ballots, CancellationToken ct = default);
}

/// <summary>
///    Plurality voting — each ballot's top-ranked option gets one point.
///    The option with the most points wins.
/// </summary>
public sealed class MajorityVotingStrategy : IVotingStrategy
{
   /// <inheritdoc />
   public string MethodName => "Majority";

   /// <inheritdoc />
   public Task<VotingResult> TallyAsync(IReadOnlyList<ParticipantBallot> ballots, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(ballots);
      ct.ThrowIfCancellationRequested();

      var scores = new Dictionary<string, double>(StringComparer.Ordinal);
      foreach (var ballot in ballots)
      {
         if (ballot.Rankings.Count == 0) continue;
         var top = ballot.Rankings.OrderBy(r => r.Rank).First();
         scores[top.Name] = scores.GetValueOrDefault(top.Name) + 1;
      }

      var result = PickWinner(scores, MethodName);
      return Task.FromResult(result);
   }

   /// <summary>Picks the winner from a score table — shared by all simple-counting strategies.</summary>
   internal static VotingResult PickWinner(Dictionary<string, double> scores, string method)
   {
      if (scores.Count == 0)
         return new VotingResult("(no votes)", 0, scores, method);

      var winner = scores.OrderByDescending(kv => kv.Value).First();
      return new VotingResult(winner.Key, winner.Value, scores, method);
   }
}

/// <summary>
///    Borda count — each ballot awards points to every ranked option: if there are N
///    options, the top-ranked gets N-1 points, the second gets N-2, and so on.
///    The option with the highest total score wins.
/// </summary>
public sealed class BordaCountVotingStrategy : IVotingStrategy
{
   /// <inheritdoc />
   public string MethodName => "BordaCount";

   /// <inheritdoc />
   public Task<VotingResult> TallyAsync(IReadOnlyList<ParticipantBallot> ballots, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(ballots);
      ct.ThrowIfCancellationRequested();

      var scores = new Dictionary<string, double>(StringComparer.Ordinal);
      foreach (var ballot in ballots)
      {
         var ranked = ballot.Rankings.OrderBy(r => r.Rank).ToList();
         var n = ranked.Count;
         for (var i = 0; i < n; i++)
         {
            var bordaPoints = n - 1 - i; // top rank (i=0) gets n-1 points
            scores[ranked[i].Name] = scores.GetValueOrDefault(ranked[i].Name) + bordaPoints;
         }
      }

      var result = MajorityVotingStrategy.PickWinner(scores, MethodName);
      return Task.FromResult(result);
   }
}

/// <summary>
///    Weighted voting — each ballot's top-ranked option gets the ballot's
///    <see cref="ParticipantBallot.Weight" /> points. Use
///    <see cref="MemberWeights" /> to give specific members more influence
///    (e.g. a SecurityExpert's vote counts double).
/// </summary>
public sealed class WeightedVotingStrategy(double defaultWeight = 1.0) : IVotingStrategy
{
   /// <summary>
   ///    Per-member weight overrides. Keyed by <see cref="ParticipantBallot.MemberName" />
   ///    (case-sensitive). When a member is not in this dictionary, the ballot's own
   ///    <see cref="ParticipantBallot.Weight" /> is used.
   /// </summary>
   public Dictionary<string, double> MemberWeights { get; init; } = new(StringComparer.Ordinal);
   /// <inheritdoc />
   public string MethodName => "Weighted";

   /// <inheritdoc />
   public Task<VotingResult> TallyAsync(IReadOnlyList<ParticipantBallot> ballots, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(ballots);
      ct.ThrowIfCancellationRequested();

      // Validate weights: must be non-negative, and at least one must be > 0.
      foreach (var (member, w) in MemberWeights)
         if (w < 0)
            throw new InvalidOperationException($"Member '{member}' has a negative weight {w}. Weights must be non-negative.");
      if (ballots.All(b => ResolveWeight(b) <= 0))
         throw new InvalidOperationException("At least one ballot must have a positive weight.");

      var scores = new Dictionary<string, double>(StringComparer.Ordinal);
      foreach (var ballot in ballots)
      {
         var weight = ResolveWeight(ballot);
         if (weight <= 0 || ballot.Rankings.Count == 0) continue;
         var top = ballot.Rankings.OrderBy(r => r.Rank).First();
         scores[top.Name] = scores.GetValueOrDefault(top.Name) + weight;
      }

      var result = MajorityVotingStrategy.PickWinner(scores, MethodName);
      return Task.FromResult(result);
   }

   private double ResolveWeight(ParticipantBallot ballot)
   {
      return MemberWeights.GetValueOrDefault(ballot.MemberName, defaultWeight);
   }
}
