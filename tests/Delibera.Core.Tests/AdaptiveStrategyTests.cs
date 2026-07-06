using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-09 Dynamic Strategy Switching feature
///    (<see cref="IStrategySelector"/>, <see cref="AdaptiveStrategySelector"/>,
///    <see cref="DebateProgress"/>, <see cref="DebateRound.StrategyUsed"/>,
///    <see cref="ICouncilBuilder.WithAdaptiveStrategy(IStrategySelector)"/>).
/// </summary>
public class AdaptiveStrategyTests
{
    [Fact]
    public void AdaptiveStrategySelector_Required_Properties_Are_Enforced_At_Compile_Time()
    {
        // The `required` keyword on Initial and OnStalemate prevents construction
        // without setting both. This is a compile-time guarantee, so we just verify
        // a correctly-constructed instance works.
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate()
        };
        selector.Initial.Should().NotBeNull();
        selector.OnStalemate.Should().NotBeNull();
    }

    [Fact]
    public async Task AdaptiveStrategySelector_Returns_Null_When_Diversity_Is_High()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 2
        };
        var progress = MakeProgress(diversity: 0.8, round: 1);

        var next = await selector.SelectNextAsync(progress);

        next.Should().BeNull();
    }

    [Fact]
    public async Task AdaptiveStrategySelector_Returns_OnStalemate_After_Threshold_Rounds()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 2,
            StagnationScore = 0.3
        };

        var p1 = MakeProgress(diversity: 0.1, round: 1);
        var p2 = MakeProgress(diversity: 0.1, round: 2);

        (await selector.SelectNextAsync(p1)).Should().BeNull();
        var next = await selector.SelectNextAsync(p2);
        next.Should().NotBeNull();
        next!.StrategyName.Should().Be(new CritiqueDebate().StrategyName);
    }

    [Fact]
    public async Task AdaptiveStrategySelector_Resets_Stagnation_Counter_When_Diversity_Recovers()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 2,
            StagnationScore = 0.3
        };

        (await selector.SelectNextAsync(MakeProgress(0.1, 1, maxRounds: 5))).Should().BeNull();
        (await selector.SelectNextAsync(MakeProgress(0.8, 2, maxRounds: 5))).Should().BeNull(); // recover → reset
        (await selector.SelectNextAsync(MakeProgress(0.1, 3, maxRounds: 5))).Should().BeNull(); // counter back to 1
        (await selector.SelectNextAsync(MakeProgress(0.1, 4, maxRounds: 5))).Should().NotBeNull(); // 2 → switch
    }

    [Fact]
    public async Task AdaptiveStrategySelector_Switches_Only_Once_Per_Debate()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 1
        };

        (await selector.SelectNextAsync(MakeProgress(0.1, 1))).Should().NotBeNull();
        (await selector.SelectNextAsync(MakeProgress(0.1, 2))).Should().BeNull(); // already switched
        (await selector.SelectNextAsync(MakeProgress(0.1, 3))).Should().BeNull();
    }

    [Fact]
    public async Task AdaptiveStrategySelector_Reset_Allows_Reuse()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 1
        };

        (await selector.SelectNextAsync(MakeProgress(0.1, 1))).Should().NotBeNull();
        selector.Reset();
        (await selector.SelectNextAsync(MakeProgress(0.1, 1))).Should().NotBeNull();
    }

    [Fact]
    public async Task AdaptiveStrategySelector_No_Switch_On_Last_Round()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 1
        };
        var progress = MakeProgress(diversity: 0.1, round: 4, maxRounds: 4);

        (await selector.SelectNextAsync(progress)).Should().BeNull();
    }

    [Fact]
    public async Task AdaptiveStrategySelector_Falls_Back_To_Text_Similarity_When_Diversity_Is_Zero()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 1,
            StagnationScore = 0.3
        };

        // Round with near-identical responses and diversity=0 (no embeddings).
        var round = new DebateRound
        {
            RoundNumber = 1,
            RoundName = "Initial",
            Responses = new Dictionary<string, string>
            {
                ["a"] = "The answer is 42 and we should ship it now.",
                ["b"] = "The answer is 42 and we should ship it now.",
            }
        };
        var progress = new DebateProgress(1, 4, [round], 0.0, false);

        (await selector.SelectNextAsync(progress)).Should().NotBeNull();
    }

    [Fact]
    public async Task AdaptiveStrategySelector_No_Switch_When_Responses_Differ_Even_With_Zero_Diversity_Score()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 1,
            StagnationScore = 0.3
        };

        var round = new DebateRound
        {
            RoundNumber = 1,
            RoundName = "Initial",
            Responses = new Dictionary<string, string>
            {
                ["a"] = "We should use microservices for scalability.",
                ["b"] = "A modular monolith is simpler and cheaper to operate."
            }
        };
        var progress = new DebateProgress(1, 4, [round], 0.0, false);

        (await selector.SelectNextAsync(progress)).Should().BeNull();
    }

    [Fact]
    public void CouncilBuilder_WithAdaptiveStrategy_Sets_Initial_Strategy()
    {
        var selector = new AdaptiveStrategySelector
        {
            Initial = new CritiqueDebate(),
            OnStalemate = new ConsensusDebate()
        };
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAdaptiveStrategy(selector)
            .Build();

        executor.Strategy.Should().BeOfType<CritiqueDebate>();
        executor.StrategySelector.Should().BeSameAs(selector);
    }

    [Fact]
    public void CouncilBuilder_WithoutAdaptiveStrategy_Leaves_Selector_Null()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.StrategySelector.Should().BeNull();
    }

    [Fact]
    public void CouncilBuilder_WithAdaptiveStrategy_Null_Throws()
    {
        var act = () => new CouncilBuilder().WithAdaptiveStrategy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_WithAdaptiveStrategy_Stamps_StrategyUsed_On_Rounds()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 99 // never switches
        };
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAdaptiveStrategy(selector)
            .Build();

        var result = await executor.ExecuteAsync();

        result.Rounds.Should().NotBeEmpty();
        result.Rounds.Should().AllSatisfy(r => r.StrategyUsed.Should().NotBeNull());
        result.Rounds.Should().AllSatisfy(r => r.StrategyUsed!.StrategyName.Should().Be(new StandardDebate().StrategyName));
    }

    [Fact]
    public async Task ExecuteAsync_WithAdaptiveStrategy_Logs_Switch_When_Triggered()
    {
        // Use a custom selector that always switches after round 1.
        var provider = new FakeLLMProvider(reply: "ok");
        var selector = new AlwaysSwitchSelector(new StandardDebate(), new CritiqueDebate());
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAdaptiveStrategy(selector)
            .Build();

        var result = await executor.ExecuteAsync();

        // The switch should be logged in execution logs.
        result.ExecutionLogs.Should().Contain(l => l.Message.Contains("Adaptive strategy switch"));
        // And the result's StrategyName should reflect the new strategy.
        result.StrategyName.Should().Be(new CritiqueDebate().StrategyName);
    }

    [Fact]
    public void DebateRound_StrategyUsed_Defaults_Null()
    {
        var round = new DebateRound
        {
            RoundNumber = 1,
            RoundName = "Test",
            Responses = new Dictionary<string, string>()
        };
        round.StrategyUsed.Should().BeNull();
    }

    [Fact]
    public async Task IStrategySelector_Custom_Implementation_Is_Invoked()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var selector = new CountingSelector();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAdaptiveStrategy(selector)
            .Build();

        await executor.ExecuteAsync();

        selector.CallCount.Should().BeGreaterThan(0);
    }

    private static DebateProgress MakeProgress(double diversity, int round, int maxRounds = 4)
    {
        var rounds = new List<DebateRound>();
        for (var i = 1; i <= round; i++)
            rounds.Add(new DebateRound
            {
                RoundNumber = i,
                RoundName = $"Round {i}",
                Responses = new Dictionary<string, string> { ["a"] = "x", ["b"] = "y" }
            });
        return new DebateProgress(round, maxRounds, rounds, diversity, false);
    }

    private sealed class AlwaysSwitchSelector(IDebateStrategy initial, IDebateStrategy next) : IStrategySelector
    {
        public int CallCount;
        public ValueTask<IDebateStrategy?> SelectNextAsync(DebateProgress progress, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CallCount);
            return new(next);
        }
    }

    private sealed class CountingSelector : IStrategySelector
    {
        public int CallCount;
        public ValueTask<IDebateStrategy?> SelectNextAsync(DebateProgress progress, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CallCount);
            return new((IDebateStrategy?)null);
        }
    }
}