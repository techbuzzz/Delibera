namespace Delibera.Core.Models;

/// <summary>
///    A participant that failed to produce an answer during a debate round.
/// </summary>
/// <remarks>
///    <para>
///       Introduced because a failed member used to be handed to the Chairman as the literal
///       string <c>"[ERROR: ...]"</c>, indistinguishable from a real opinion. A verdict
///       synthesised from a partial council looked identical to a complete one, so callers had
///       no way to know that a member was silently missing.
///    </para>
///    <para>
///       Failed members are excluded from <see cref="DebateRound.Responses" /> and recorded here
///       instead, which makes <see cref="DebateResult.FailedMembers" /> and
///       <see cref="DebateResult.IsDegraded" /> reliable signals.
///    </para>
/// </remarks>
/// <param name="RoundNumber">1-based round in which the failure occurred.</param>
/// <param name="RoundName">Round title at the time of the failure.</param>
/// <param name="Role">Member role.</param>
/// <param name="DisplayName">Member display name.</param>
/// <param name="Model">Model the member was using.</param>
/// <param name="Error">Failure message.</param>
public sealed record MemberFailure(
   int RoundNumber,
   string RoundName,
   string Role,
   string DisplayName,
   string Model,
   string Error)
{
   /// <summary>Renders one line suitable for an execution log or a report row.</summary>
   public override string ToString() =>
      $"Round {RoundNumber} ({RoundName}): {DisplayName} [{Model}] failed - {Error}";
}