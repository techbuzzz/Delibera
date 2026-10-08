using Delibera.Core.Cost;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    S-02 — cost gates, rate limiting and spend reporting.
///
///    The behaviour that matters is what happens at the ceiling. A gate that throws takes the
///    debate down and leaves the operator with no record of what was already spent, so a denial
///    must produce a degraded result carrying the spend instead.
/// </summary>
public sealed class CostGateTests
{
   [Fact]
   public async Task BudgetCostGate_Allows_While_Under_The_Ceiling()
   {
      var gate = new BudgetCostGate(1.00m);

      var decision = await gate.CheckAsync(Spend(0.25m));

      decision.IsAllowed.Should().BeTrue();
      decision.SpentSoFar.Should().Be(0.25m);
   }

   [Fact]
   public async Task BudgetCostGate_Denies_Once_The_Ceiling_Is_Reached()
   {
      var gate = new BudgetCostGate(1.00m);

      var decision = await gate.CheckAsync(Spend(1.40m));

      decision.IsAllowed.Should().BeFalse();
      decision.Limit.Should().Be(1.00m);
      decision.SpentSoFar.Should().Be(1.40m, "the caller needs to see the overspend, not just the limit");
      decision.Reason.Should().NotBeNullOrWhiteSpace();
   }

   [Fact]
   public async Task BudgetCostGate_WarnAndContinue_Lets_The_Debate_Run()
   {
      var gate = new BudgetCostGate(1.00m, CostLimitBehavior.WarnAndContinue);

      (await gate.CheckAsync(Spend(2m))).IsAllowed.Should().BeTrue();
   }

   [Fact]
   public void BudgetCostGate_Rejects_A_NonPositive_Ceiling()
   {
      var act = () => new BudgetCostGate(0m);

      act.Should().Throw<ArgumentOutOfRangeException>();
   }

   [Fact]
   public async Task RateLimiter_Throw_Behaviour_Refuses_Once_The_Window_Is_Full()
   {
      var limiter = new TokenBucketRateLimiter(new RateLimitPolicy(
         2, TimeSpan.FromMinutes(5), RateLimitBehavior.Throw, RateLimitScope.Global));

      await limiter.AcquireAsync("p", "m", "alice");
      await limiter.AcquireAsync("p", "m", "bob");

      var act = async () => await limiter.AcquireAsync("p", "m", "carol");

      await act.Should().ThrowAsync<RateLimitExceededException>();
   }

   [Fact]
   public async Task RateLimiter_Drop_Behaviour_Refuses_Instead_Of_Waiting()
   {
      var limiter = new TokenBucketRateLimiter(new RateLimitPolicy(
         1, TimeSpan.FromMinutes(5), RateLimitBehavior.Drop, RateLimitScope.Global));

      await limiter.AcquireAsync("p", "m", "alice");

      var act = async () => await limiter.AcquireAsync("p", "m", "bob");

      await act.Should().ThrowAsync<RateLimitExceededException>()
         .WithMessage("*Drop*");
   }

   [Fact]
   public async Task RateLimiter_PerModel_Scope_Keeps_Different_Models_Independent()
   {
      var limiter = new TokenBucketRateLimiter(new RateLimitPolicy(
         1, TimeSpan.FromMinutes(5), RateLimitBehavior.Throw, RateLimitScope.PerModel));

      await limiter.AcquireAsync("p", "model-a", "alice");

      // Same model would be refused; a different model has its own budget.
      await limiter.AcquireAsync("p", "model-b", "alice");

      var act = async () => await limiter.AcquireAsync("p", "model-a", "bob");
      await act.Should().ThrowAsync<RateLimitExceededException>();
   }

   [Fact]
   public async Task RateLimiter_Queue_Behaviour_Throttles_N_Calls_Under_Concurrency_Without_Deadlocking()
   {
      // 4 permits in a short window, 16 concurrent callers: the queue has to actually queue and
      // then drain, without a thread pinned and without a waiter that can never be released.
      var limiter = new TokenBucketRateLimiter(new RateLimitPolicy(
         4, TimeSpan.FromMilliseconds(300), RateLimitBehavior.Queue, RateLimitScope.Global));

      var sw = System.Diagnostics.Stopwatch.StartNew();
      var callers = Enumerable.Range(0, 16)
         .Select(i => Task.Run(async () =>
         {
            await limiter.AcquireAsync("p", "m", $"member-{i}");
            return i;
         }))
         .ToArray();

      var completed = await Task.WhenAll(callers);
      sw.Stop();

      completed.Should().HaveCount(16);
      completed.Should().OnlyHaveUniqueItems();
      // Generous upper bound: this asserts it finished, not that it was fast.
      sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
   }

   [Fact]
   public void ModelPricingRegistry_Prefers_An_Exact_Match_Over_A_Substring_Pattern()
   {
      var registry = new ModelPricingRegistry();
      registry.Register(new ModelPricing("gpt-4o-mini", 0.15m, 0.60m));
      registry.Register(new ModelPricing("gpt-4o", 2.50m, 10.00m));

      registry.TryGetPricing("gpt-4o", out var exact).Should().BeTrue();
      exact.InputPerMillionTokens.Should().Be(2.50m);

      registry.TryGetPricing("gpt-4o-2024-11-20", out var byPattern).Should().BeTrue();
      byPattern.InputPerMillionTokens.Should().Be(2.50m,
         "a dated variant should fall back to the base model's price");
   }

   [Fact]
   public void ModelPricingRegistry_Reports_An_Unknown_Model_Rather_Than_Returning_Zero()
   {
      var registry = new ModelPricingRegistry();

      var found = registry.TryGetPricing("some-unlisted-model", out var pricing);

      found.Should().BeFalse();
      pricing.Should().NotBeNull();
   }

   [Fact]
   public void ModelPricingRegistry_Reads_A_Json_Price_Table()
   {
      const string json = """
         [
           { "model": "claude", "inputPerMillion": 3.0, "outputPerMillion": 15.0 },
           { "model": "gpt-4o", "inputPerMillion": 2.5, "outputPerMillion": 10.0 }
         ]
         """;

      var registry = new ModelPricingRegistry(json);

      registry.TryGetPricing("gpt-4o", out var pricing).Should().BeTrue();
      pricing.OutputPerMillionTokens.Should().Be(10.0m);
      registry.TryGetPricing("claude-sonnet", out var claude).Should().BeTrue();
      claude.InputPerMillionTokens.Should().Be(3.0m);
   }

   [Fact]
   public void CostLedger_Accumulates_Per_Member_And_Reports_A_Cost()
   {
      var registry = new ModelPricingRegistry();
      registry.Register(new ModelPricing("model-a", 1_000m, 2_000m));

      var ledger = new CostLedger(registry);
      ledger.RecordCall("alice", "p", "model-a", "prompt text", "a response of some length");

      var estimate = ledger.Build();

      estimate.Members.Should().ContainSingle();
      var member = estimate.Members[0];
      member.MemberName.Should().Be("alice");
      member.ModelName.Should().Be("model-a");
      member.CallCount.Should().Be(1);
      member.PromptTokens.Should().BeGreaterThan(0);
      member.CompletionTokens.Should().BeGreaterThan(0);
      member.Cost.Should().BeGreaterThan(0m);
      member.IsEstimate.Should().BeFalse("the price was registered exactly");
      estimate.IsEstimate.Should().BeFalse();
      estimate.WasTruncated.Should().BeFalse();
   }

   [Fact]
   public void CostLedger_Flags_An_Unregistered_Model_As_An_Estimate()
   {
      var ledger = new CostLedger(new ModelPricingRegistry());

      ledger.RecordCall("alice", "p", "unlisted", "prompt", "response");

      var estimate = ledger.Build();

      estimate.IsEstimate.Should().BeTrue(
         "an unregistered model must not read as a free model");
   }

   [Fact]
   public void CostLedger_Accumulates_Across_Concurrent_Calls()
   {
      var ledger = new CostLedger();

      Parallel.For(0, 50, _ => ledger.RecordCall("alice", "p", "m", "prompt", "response"));

      var estimate = ledger.Build();
      estimate.Members.Should().ContainSingle();
      estimate.Members[0].CallCount.Should().Be(50);
      estimate.TotalCompletionTokens.Should().BeGreaterThan(0);
   }

   [Fact]
   public void CostLedger_Snapshot_Is_Stable_Once_Built()
   {
      var ledger = new CostLedger();
      ledger.RecordCall("alice", "p", "m", "prompt", "response");
      var snapshot = ledger.Build();

      ledger.RecordCall("alice", "p", "m", "prompt", "response");

      snapshot.Members[0].CallCount.Should().Be(1, "a snapshot must not mutate as new calls land");
   }

   [Fact]
   public void DebateExecutionOptions_Reuses_One_Ledger_Across_Rounds()
   {
      var options = new DebateExecutionOptions();

      var first = options.GetOrCreateLedger();
      var second = options.GetOrCreateLedger();

      second.Should().BeSameAs(first,
         "every round and every member task has to accumulate into one estimate");
   }

   [Fact]
   public void CostEstimate_Empty_Reports_Zero_Without_Claims()
   {
      var empty = CostEstimate.Empty;

      empty.TotalCost.Should().Be(0m);
      empty.IsEstimate.Should().BeFalse();
      empty.WasTruncated.Should().BeFalse();
      empty.AveragePerMember.Should().Be(0m);
   }

   private static CostEstimate Spend(decimal amount) => new(
      amount, 1000, 500, [], IsEstimate: false, WasTruncated: false);
}