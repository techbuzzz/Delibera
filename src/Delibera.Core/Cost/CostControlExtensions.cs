using Delibera.Core.Interfaces;

namespace Delibera.Core.Cost;

/// <summary>
///    Cost-control configuration for the fluent council-building chain.
/// </summary>
/// <remarks>
///    <para>
///    The fluent chain is typed to <see cref="ICouncilBuilder" />: every existing
///    <c>With…</c> method returns the interface, so a method that only exists on the concrete
///    <see cref="Council.CouncilBuilder" /> would be unreachable after the first call —
///    <c>new CouncilBuilder().WithSystemPrompt(…).WithCostLimit(…)</c> would not compile.
///    </para>
///    <para>
///    Extension methods fix that without touching the interface. Adding an abstract member to
///    <see cref="ICouncilBuilder" /> would be a source-breaking change for anyone who
///    implemented it, and 10.5.x is a backwards-compatible line; adding a member <em>to</em> the
///    interface was ruled out for the same reason. An extension method is neither, so the
///    interface stays byte-for-byte as it was and the chain keeps working.
///    </para>
///    <para>
///    A builder that is not a <see cref="Council.CouncilBuilder" /> — a custom
///    <see cref="ICouncilBuilder" /> implementation — reports a clear
///    <see cref="NotSupportedException" /> rather than silently ignoring the setting. Silently
///    dropping a cost ceiling is the one outcome that must never happen: the caller would
///    believe they had bounded the spend.
///    </para>
/// </remarks>
public static class CostControlExtensions
{
   /// <inheritdoc cref="Council.CouncilBuilder.WithCostLimit(decimal, CostLimitBehavior)" />
   public static ICouncilBuilder WithCostLimit(
      this ICouncilBuilder builder,
      decimal limit,
      CostLimitBehavior behavior = CostLimitBehavior.Abort)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithCostLimit(limit, behavior);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithCostGate(ICostGate)" />
   public static ICouncilBuilder WithCostGate(this ICouncilBuilder builder, ICostGate gate)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithCostGate(gate);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithRateLimit(int, TimeSpan, RateLimitBehavior, RateLimitScope)" />
   public static ICouncilBuilder WithRateLimit(
      this ICouncilBuilder builder,
      int callsPerWindow,
      TimeSpan window,
      RateLimitBehavior behavior = RateLimitBehavior.Queue,
      RateLimitScope scope = RateLimitScope.PerModel)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithRateLimit(callsPerWindow, window, behavior, scope);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithRateLimiter(IRateLimiter)" />
   public static ICouncilBuilder WithRateLimiter(this ICouncilBuilder builder, IRateLimiter limiter)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithRateLimiter(limiter);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithPricingRegistry(IModelPricingRegistry)" />
   public static ICouncilBuilder WithPricingRegistry(this ICouncilBuilder builder, IModelPricingRegistry registry)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithPricingRegistry(registry);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithCompressionThreshold(int)" />
   public static ICouncilBuilder WithCompressionThreshold(this ICouncilBuilder builder, int tokens)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithCompressionThreshold(tokens);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithTools(IToolProvider)" />
   public static ICouncilBuilder WithTools(this ICouncilBuilder builder, IToolProvider provider)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithTools(provider);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithTools(AIFunction[])" />
   public static ICouncilBuilder WithTools(this ICouncilBuilder builder, params AIFunction[] tools)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithTools(tools);
   }

   /// <inheritdoc cref="Council.CouncilBuilder.WithMaxToolIterations(int)" />
   public static ICouncilBuilder WithMaxToolIterations(this ICouncilBuilder builder, int iterations)
   {
      ArgumentNullException.ThrowIfNull(builder);
      return AsConcrete(builder).WithMaxToolIterations(iterations);
   }

   private static Council.CouncilBuilder AsConcrete(ICouncilBuilder builder) => builder as Council.CouncilBuilder
      ?? throw new NotSupportedException(
         $"{builder.GetType().Name} does not support cost control. Cost ceilings and rate limits are "
         + "implemented by Delibera.Core's CouncilBuilder; a custom ICouncilBuilder has to apply "
         + "them through DebateExecutionOptions instead. Ignoring the request would leave a cost "
         + "ceiling that looks configured but is not enforced.");
}