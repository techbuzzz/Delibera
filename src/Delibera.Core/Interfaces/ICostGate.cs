namespace Delibera.Core.Interfaces;

/// <summary>
///    Decides whether a debate may spend one more model call.
/// </summary>
/// <remarks>
///    A gate is consulted before <em>each</em> member call, so it can stop a runaway debate at
///    the next call rather than after the fact. Implementations must not throw for a denial: a
///    gate that throws takes the whole debate down and leaves the operator with no record of the
///    spend that already happened.
/// </remarks>
public interface ICostGate
{
   /// <summary>
   ///    Asks whether the next model call is within budget.
   /// </summary>
   /// <param name="current">Spend accumulated so far, including the in-flight round.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>A decision; see <see cref="CostGateDecision" />.</returns>
   ValueTask<CostGateDecision> CheckAsync(CostEstimate current, CancellationToken ct = default);
}

/// <summary>
///    Throttles model calls before they are issued.
/// </summary>
public interface IRateLimiter
{
   /// <summary>
   ///    Waits for, or refuses, capacity for one call.
   /// </summary>
   /// <param name="provider">Provider name, used to bucket by <see cref="RateLimitScope" />.</param>
   /// <param name="model">Model name.</param>
   /// <param name="memberName">Member display name.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <exception cref="RateLimitExceededException">
   ///    Thrown when the configured <see cref="RateLimitBehavior.Throw" /> applies and no
   ///    capacity is available.
   /// </exception>
   /// <returns>
   ///    A task that completes when the call may proceed. Implementations must not block a
   ///    thread while waiting.
   /// </returns>
   ValueTask AcquireAsync(
      string provider,
      string model,
      string memberName,
      CancellationToken ct = default);
}

/// <summary>
///    Resolves per-token prices for models.
/// </summary>
public interface IModelPricingRegistry
{
   /// <summary>
   ///    Looks up the price for a model.
   /// </summary>
   /// <param name="modelName">Model identifier, as reported by the provider.</param>
   /// <param name="pricing">The registered price, when one exists.</param>
   /// <returns><c>true</c> when an exact or pattern match was found.</returns>
   bool TryGetPricing(string modelName, out ModelPricing pricing);

   /// <summary>
   ///    Registers or replaces a model's price.
   /// </summary>
   /// <param name="pricing">Price to register.</param>
   void Register(ModelPricing pricing);
}

/// <summary>
///    Raised when a rate limiter with <see cref="RateLimitBehavior.Throw" /> refuses a call.
/// </summary>
public sealed class RateLimitExceededException : InvalidOperationException
{
   /// <summary>Creates the exception with a message describing the exhausted limit.</summary>
   /// <param name="message">Operator-facing description.</param>
   public RateLimitExceededException(string message) : base(message)
   {
   }
}