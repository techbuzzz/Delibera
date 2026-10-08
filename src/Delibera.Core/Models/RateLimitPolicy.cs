namespace Delibera.Core.Models;

/// <summary>
///    What a cost gate decided when the debate asked permission to make another model call.
/// </summary>
/// <param name="IsAllowed">Whether the call may proceed.</param>
/// <param name="SpentSoFar">Spend accumulated at the moment of the decision.</param>
/// <param name="Limit">The configured ceiling, or <c>null</c> when the gate is unbounded.</param>
/// <param name="Reason">Operator-facing explanation, suitable for a log line or an API response.</param>
public sealed record CostGateDecision(
   bool IsAllowed,
   decimal SpentSoFar,
   decimal? Limit,
   string? Reason)
{
   /// <summary>Allows the call, unconditionally.</summary>
   public static CostGateDecision Allow(decimal spentSoFar)
   {
      return new CostGateDecision(true, spentSoFar, null, null);
   }

   /// <summary>
   ///    Denies the call. The caller keeps a degraded result rather than throwing, because a
   ///    cost ceiling that throws leaves the operator with no report of what was already spent.
   /// </summary>
   /// <param name="spentSoFar">Spend accumulated at the moment of the decision.</param>
   /// <param name="limit">
   ///    The ceiling that was crossed, or <c>null</c> for a token gate, which has no money figure.
   /// </param>
   /// <param name="reason">Operator-facing explanation.</param>
   public static CostGateDecision Deny(decimal spentSoFar, decimal? limit, string reason)
   {
      return new CostGateDecision(false, spentSoFar, limit, reason);
   }
}

/// <summary>
///    What to do when the cost ceiling would be crossed.
/// </summary>
public enum CostLimitBehavior
{
   /// <summary>
   ///    Stop making model calls and return a degraded result carrying the spend so far.
   ///    The default: the debate ends early, but the operator can still see what it cost.
   /// </summary>
   Abort = 0,

   /// <summary>
   ///    Log one warning when the ceiling is crossed and let the debate run to completion.
   /// </summary>
   WarnAndContinue = 1,

   /// <summary>
   ///    Let the debate run and report the overrun only in the final
   ///    <see cref="CostEstimate" />.
   /// </summary>
   Ignore = 2
}

/// <summary>
///    What to do when a rate limiter has no capacity left.
/// </summary>
public enum RateLimitBehavior
{
   /// <summary>
   ///    Asynchronously wait for capacity. The default: throttling that does not pin a thread
   ///    and does not drop work.
   /// </summary>
   Queue = 0,

   /// <summary>Throw immediately so the member is recorded as failed.</summary>
   Throw = 1,

   /// <summary>Skip the call and record the member as failed without waiting.</summary>
   Drop = 2
}

/// <summary>
///    What a rate limit is applied to.
/// </summary>
public enum RateLimitScope
{
   /// <summary>One shared bucket for the whole debate.</summary>
   Global = 0,

   /// <summary>One bucket per provider, shared across models.</summary>
   PerProvider = 1,

   /// <summary>One bucket per provider and model pair.</summary>
   PerModel = 2,

   /// <summary>One bucket per member display name.</summary>
   PerMember = 3
}

/// <summary>
///    How many model calls may run inside a rolling window, and what happens when the
///    window is full.
/// </summary>
/// <param name="PermitLimit">Calls permitted per <paramref name="Window" />.</param>
/// <param name="Window">Length of the rolling window.</param>
/// <param name="Behavior">What to do when the window is exhausted.</param>
/// <param name="Scope">What the limit applies to.</param>
public sealed record RateLimitPolicy(
   int PermitLimit,
   TimeSpan Window,
   RateLimitBehavior Behavior = RateLimitBehavior.Queue,
   RateLimitScope Scope = RateLimitScope.PerModel)
{
   /// <summary>
   ///    Creates a policy permitting <paramref name="callsPerWindow" /> calls per minute.
   /// </summary>
   /// <param name="callsPerWindow">Permit count; must be positive.</param>
   /// <param name="window">Window length; must be positive.</param>
   /// <param name="behavior">What to do when exhausted.</param>
   /// <param name="scope">What the limit applies to.</param>
   /// <exception cref="ArgumentOutOfRangeException">The permit count or window is not positive.</exception>
   public static RateLimitPolicy PerMinute(
      int callsPerWindow,
      TimeSpan window,
      RateLimitBehavior behavior = RateLimitBehavior.Queue,
      RateLimitScope scope = RateLimitScope.PerModel)
   {
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(callsPerWindow);
      if (window <= TimeSpan.Zero)
         throw new ArgumentOutOfRangeException(nameof(window), window, "Window must be positive.");

      return new RateLimitPolicy(callsPerWindow, window, behavior, scope);
   }
}
