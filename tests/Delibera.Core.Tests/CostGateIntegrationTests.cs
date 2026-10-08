using Delibera.Core.Cost;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    End-to-end checks that the cost plumbing is actually wired into the debate loop, not just
///    present as types. A gate that is never consulted protects nothing.
/// </summary>
public sealed class CostGateIntegrationTests
{
   [Fact]
   public async Task A_Debate_Under_Its_Ceiling_Reports_Its_Spend_And_Is_Not_Degraded()
   {
      var registry = new ModelPricingRegistry();
      registry.Register(new ModelPricing("model-a", 1m, 2m));
      registry.Register(new ModelPricing("model-b", 1m, 2m));

      var executor = BuildCouncil(costLimit: 100m, registry: registry);

      var result = await executor.ExecuteAsync();

      result.CostEstimate.Should().NotBeNull("a configured ceiling must always report what it protected");
      result.CostEstimate!.WasTruncated.Should().BeFalse();
      result.CostEstimate.Members.Should().NotBeEmpty();
      result.CostEstimate.TotalPromptTokens.Should().BeGreaterThan(0);
      result.CostEstimate.IsEstimate.Should().BeFalse("both models had exact registered prices");
      result.IsDegraded.Should().BeFalse();
   }

   [Fact]
   public async Task A_Cost_Ceiling_Stops_Calling_Models_And_Reports_The_Spend()
   {
      var registry = new ModelPricingRegistry();
      registry.Register(new ModelPricing("model-a", 1_000_000m, 2_000_000m));
      registry.Register(new ModelPricing("model-b", 1_000_000m, 2_000_000m));

      // A ceiling the very first call blows through: the first call has to be allowed (its cost
      // is unknowable before it is made), every call after it must be refused.
      var executor = BuildCouncil(costLimit: 0.0000001m, registry: registry);

      var result = await executor.ExecuteAsync();

      result.Should().NotBeNull("a cost ceiling that throws leaves the operator with no report at all");
      result.CostEstimate.Should().NotBeNull();
      result.CostEstimate!.WasTruncated.Should().BeTrue();
      result.CostEstimate.TotalCost.Should().BeGreaterThan(0m, "the spend that triggered the gate must be visible");
      result.IsDegraded.Should().BeTrue(
         "skipped member calls have to be visible as failures, not silently absent");
   }

   [Fact]
   public async Task A_Debate_Without_A_Cost_Limit_Leaves_CostEstimate_Unset()
   {
      var executor = BuildCouncil(costLimit: null);

      var result = await executor.ExecuteAsync();

      result.CostEstimate.Should().BeNull(
         "token counts are only an estimate of the provider's bill, so they are reported explicitly");
   }

   [Fact]
   public async Task A_Registered_Price_Turns_Token_Counts_Into_Money()
   {
      var registry = new ModelPricingRegistry();
      registry.Register(new ModelPricing("model-a", 1_000_000m, 2_000_000m));
      registry.Register(new ModelPricing("model-b", 1_000_000m, 2_000_000m));

      var executor = BuildCouncil(costLimit: null, registry: registry);

      var result = await executor.ExecuteAsync();

      result.CostEstimate.Should().NotBeNull();
      result.CostEstimate!.IsEstimate.Should().BeFalse("both models had exact registered prices");
      result.CostEstimate.TotalCost.Should().BeGreaterThan(0m);
   }

   private static ICouncilExecutor BuildCouncil(decimal? costLimit, IModelPricingRegistry? registry = null)
   {
      ICouncilBuilder builder = new CouncilBuilder()
         .WithSystemPrompt("You are a council member.")
         .WithStandardDebate()
         .WithUserPrompt("Should we ship?")
         .WithMaxRounds(2)
         .AddMember("model-a", new FakeLLMProvider("Fake", "position a", chatDelayMs: 5))
         .AddMember("model-b", new FakeLLMProvider("Fake", "position b", chatDelayMs: 5));

      if (costLimit is { } limit)
         builder = builder.WithCostLimit(limit);

      if (registry is not null)
         builder = builder.WithPricingRegistry(registry);

      return builder.Build();
   }
}