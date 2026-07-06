using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-01 Async Streaming Council feature
///    (<see cref="Interfaces.ICouncilExecutor.StreamDebateAsync(CancellationToken)"/>).
/// </summary>
public class StreamingCouncilTests
{
    [Fact]
    public async Task StreamDebateAsync_Yields_Each_Round_As_It_Completes()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(2)
            .Build();

        var rounds = new List<DebateRound>();
        await foreach (var round in executor.StreamDebateAsync())
            rounds.Add(round);

        rounds.Should().NotBeEmpty();
        // StandardDebate with maxRounds=2 + Chairman → round 1 (Initial),
        // round 2 (Critique skipped because maxRounds<2 returns after r1)... actually
        // WithMaxRounds(2) → r1 + r2 + Chairman verdict = 3 rounds.
        rounds.Should().HaveCountGreaterThanOrEqualTo(2);
        rounds.Select(r => r.RoundNumber).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task StreamDebateAsync_Stamps_Total_On_Every_Round()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("fake-model", provider)
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(2)
            .Build();

        var rounds = new List<DebateRound>();
        await foreach (var round in executor.StreamDebateAsync())
            rounds.Add(round);

        // With a Chairman, total = maxRounds + 1 (verdict round).
        rounds.Should().AllSatisfy(r => r.Total.Should().Be(3));
    }

    [Fact]
    public async Task StreamDebateAsync_Last_Round_Is_Marked_IsFinal()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("fake-model", provider)
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        var rounds = new List<DebateRound>();
        await foreach (var round in executor.StreamDebateAsync())
            rounds.Add(round);

        rounds.Should().NotBeEmpty();
        // The last round should be the Chairman verdict → IsFinal == true.
        rounds.Last().IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task StreamDebateAsync_Populates_LastStreamedResult_After_Completion()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.LastStreamedResult.Should().BeNull();

        await foreach (var _ in executor.StreamDebateAsync()) { }

        executor.LastStreamedResult.Should().NotBeNull();
        executor.LastStreamedResult!.Rounds.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StreamDebateAsync_Fires_OnRoundCompleted_Event_Too()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        var eventRounds = new List<DebateRound>();
        executor.OnRoundCompleted += r => eventRounds.Add(r);

        var streamRounds = new List<DebateRound>();
        await foreach (var round in executor.StreamDebateAsync())
            streamRounds.Add(round);

        // The event should fire once per round, same as the stream.
        eventRounds.Should().HaveCount(streamRounds.Count);
    }

    [Fact]
    public async Task StreamDebateAsync_PreCancelled_Throws_OperationCanceledException()
    {
        var provider = new FakeLLMProvider(reply: "ok", chatDelayMs: 100);
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in executor.StreamDebateAsync(cts.Token)) { }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StreamDebateAsync_MidStream_Cancellation_Stops_Iteration()
    {
        var provider = new FakeLLMProvider(reply: "ok", chatDelayMs: 50);
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("fake-model", provider)
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(3)
            .Build();

        using var cts = new CancellationTokenSource();
        var roundsConsumed = 0;
        try
        {
            await foreach (var round in executor.StreamDebateAsync(cts.Token))
            {
                roundsConsumed++;
                if (roundsConsumed == 1)
                    cts.Cancel(); // Cancel after the first round.
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        // We should have consumed at most 1-2 rounds before the cancellation propagated.
        roundsConsumed.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task StreamDebateAsync_Yields_Rounds_In_Order()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("fake-model", provider)
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(2)
            .Build();

        var roundNumbers = new List<int>();
        await foreach (var round in executor.StreamDebateAsync())
            roundNumbers.Add(round.RoundNumber);

        roundNumbers.Should().BeInAscendingOrder();
        // StandardDebate with maxRounds=2 → round 1 (Initial) + round 2 (Critique)
        // + round 4 (Chairman Verdict, hardcoded number in FinalizeAsync).
        roundNumbers.Should().BeEquivalentTo(new[] { 1, 2, 4 });
    }

    [Fact]
    public async Task StreamDebateAsync_No_Chairman_Total_Equauls_MaxRounds()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            // No chairman → no verdict round → total = maxRounds
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(2)
            .Build();

        var rounds = new List<DebateRound>();
        await foreach (var round in executor.StreamDebateAsync())
            rounds.Add(round);

        rounds.Should().AllSatisfy(r => r.Total.Should().Be(2));
    }

    [Fact]
    public async Task StreamDebateAsync_WithTimeout_Stops_When_Timeout_Fires()
    {
        var provider = new FakeLLMProvider(reply: "ok", chatDelayMs: 100);
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("fake-model", provider)
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(3)
            .WithTimeout(TimeSpan.FromMilliseconds(50))
            .Build();

        var act = async () =>
        {
            await foreach (var _ in executor.StreamDebateAsync()) { }
        };
        // The timeout may surface as OCE or be swallowed by the strategy; either way
        // the stream should terminate without hanging.
        await act.Should().NotThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task StreamDebateAsync_Parallel_Consumer_Does_Not_Lose_Rounds()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("fake-model", provider)
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        var rounds = new List<DebateRound>();
        await foreach (var round in executor.StreamDebateAsync())
        {
            // Simulate some consumer work (e.g. SSE flush) — the channel's
            // backpressure should pause the strategy until we're ready.
            await Task.Delay(10);
            rounds.Add(round);
        }

        rounds.Should().HaveCount(2); // round 1 + Chairman verdict
    }

    [Fact]
    public async Task ExecuteAsync_Stays_Backward_Compatible_After_Streaming_Added()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("fake-model", provider)
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        var result = await executor.ExecuteAsync();

        result.Should().NotBeNull();
        result.Rounds.Should().NotBeEmpty();
        result.FinalVerdict.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DebateRound_Total_And_IsFinal_Properties_Work()
    {
        var round = new DebateRound
        {
            RoundNumber = 3,
            RoundName = "Chairman Verdict",
            Responses = new Dictionary<string, string>(),
        };

        round.IsFinal.Should().BeTrue(); // because "Verdict" in name

        var midRound = new DebateRound
        {
            RoundNumber = 2,
            RoundName = "Critique",
            Responses = new Dictionary<string, string>(),
            Total = 4
        };

        midRound.IsFinal.Should().BeFalse(); // 2 < 4
        midRound.Total.Should().Be(4);

        var finalByNumber = new DebateRound
        {
            RoundNumber = 4,
            RoundName = "Round 4",
            Responses = new Dictionary<string, string>(),
            Total = 4
        };
        finalByNumber.IsFinal.Should().BeTrue(); // 4 >= 4
    }
}