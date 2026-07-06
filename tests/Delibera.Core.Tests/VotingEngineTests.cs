using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using Delibera.Core.Voting;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-02 Pluggable Vote / Consensus Engine feature
///    (<see cref="IVotingStrategy"/>, <see cref="MajorityVotingStrategy"/>,
///    <see cref="BordaCountVotingStrategy"/>, <see cref="WeightedVotingStrategy"/>,
///    <see cref="Council.Chairman.CreateVoting"/>, <see cref="DebateResult.VotingTally"/>,
///    <see cref="ICouncilBuilder.WithVotingChairman"/>).
/// </summary>
public class VotingEngineTests
{
    // ── Strategy unit tests ──

    [Fact]
    public async Task MajorityVoting_Picks_TopRanked_Option_With_Most_Votes()
    {
        var ballots = new List<ParticipantBallot>
        {
            MakeBallot("A", [("X", 1), ("Y", 2), ("Z", 3)]),
            MakeBallot("B", [("X", 1), ("Z", 2), ("Y", 3)]),
            MakeBallot("C", [("Y", 1), ("X", 2), ("Z", 3)])
        };
        var strategy = new MajorityVotingStrategy();

        var result = await strategy.TallyAsync(ballots);

        result.Method.Should().Be("Majority");
        result.WinningOption.Should().Be("X");
        result.Score.Should().Be(2); // A and B ranked X first
        result.Scores["X"].Should().Be(2);
        result.Scores["Y"].Should().Be(1);
    }

    [Fact]
    public async Task BordaCount_Awards_Points_By_Rank_Position()
    {
        // 3 options → top rank gets 2 points, second gets 1, third gets 0.
        var ballots = new List<ParticipantBallot>
        {
            MakeBallot("A", [("X", 1), ("Y", 2), ("Z", 3)]), // X=2, Y=1, Z=0
            MakeBallot("B", [("Y", 1), ("X", 2), ("Z", 3)]), // Y=2, X=1, Z=0
            MakeBallot("C", [("Z", 1), ("X", 2), ("Y", 3)])  // Z=2, X=1, Y=0
        };
        // X total: 2+1+1=4, Y total: 1+2+0=3, Z total: 0+0+2=2 → X wins
        var strategy = new BordaCountVotingStrategy();

        var result = await strategy.TallyAsync(ballots);

        result.Method.Should().Be("BordaCount");
        result.WinningOption.Should().Be("X");
        result.Score.Should().Be(4);
        result.Scores["Y"].Should().Be(3);
        result.Scores["Z"].Should().Be(2);
    }

    [Fact]
    public async Task WeightedVoting_Uses_MemberWeights_Override()
    {
        var strategy = new WeightedVotingStrategy
        {
            MemberWeights = { ["A"] = 2.0, ["B"] = 1.0 }
        };
        var ballots = new List<ParticipantBallot>
        {
            MakeBallot("A", [("X", 1)]),  // weight 2.0 → X gets 2
            MakeBallot("B", [("Y", 1)]),  // weight 1.0 → Y gets 1
            MakeBallot("C", [("Y", 1)])   // not in MemberWeights → ballot weight 1.0 → Y gets 1
        };
        // X=2, Y=2 → tie, pick first by ordering → depends on dict order; both score 2
        var result = await strategy.TallyAsync(ballots);

        result.Method.Should().Be("Weighted");
        result.Scores["X"].Should().Be(2.0);
        result.Scores["Y"].Should().Be(2.0);
        result.Score.Should().Be(2.0);
    }

    [Fact]
    public async Task WeightedVoting_Negative_Weight_Throws()
    {
        var strategy = new WeightedVotingStrategy
        {
            MemberWeights = { ["A"] = -1.0 }
        };
        var ballots = new List<ParticipantBallot> { MakeBallot("A", [("X", 1)]) };

        var act = async () => await strategy.TallyAsync(ballots);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*negative weight*");
    }

    [Fact]
    public async Task WeightedVoting_All_Zero_Weights_Throws()
    {
        var strategy = new WeightedVotingStrategy();
        var ballots = new List<ParticipantBallot>
        {
            MakeBallot("A", [("X", 1)], weight: 0),
            MakeBallot("B", [("Y", 1)], weight: 0)
        };

        var act = async () => await strategy.TallyAsync(ballots);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*positive weight*");
    }

    [Fact]
    public async Task Majority_With_No_Ballots_Returns_NoVotes_Result()
    {
        var strategy = new MajorityVotingStrategy();
        var result = await strategy.TallyAsync([]);
        result.WinningOption.Should().Be("(no votes)");
        result.Score.Should().Be(0);
    }

    [Fact]
    public async Task Majority_Ignores_Ballots_With_No_Rankings()
    {
        var strategy = new MajorityVotingStrategy();
        var ballots = new List<ParticipantBallot>
        {
            MakeBallot("A", [("X", 1)]),
            MakeBallot("B", []) // empty rankings
        };
        var result = await strategy.TallyAsync(ballots);
        result.Scores.Should().HaveCount(1);
        result.Scores["X"].Should().Be(1);
    }

    [Fact]
    public void Chairman_CreateVoting_Encodes_Strategy_In_Persona()
    {
        var provider = new FakeLLMProvider();
        var member = Chairman.CreateVoting("model", provider, new MajorityVotingStrategy());

        member.Role.Should().Be("Voting Chairman");
        // PersonaPrompt is the backing field; check via the constructor-encoded value.
        // The marker prefix identifies it as a voting chairman.
        member.PersonaPrompt.Should().StartWith(Chairman.VotingChairmanMarker);
    }

    [Fact]
    public void CouncilBuilder_WithVotingChairman_Sets_VotingStrategy_And_Chairman()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithVotingChairman("chairman", provider, new BordaCountVotingStrategy())
            .Build();

        executor.VotingStrategy.Should().NotBeNull();
        executor.VotingStrategy.Should().BeOfType<BordaCountVotingStrategy>();
        executor.Chairman.Should().NotBeNull();
        // SetChairman overrides Role to "Chairman"; the voting marker is in PersonaPrompt.
        executor.Chairman!.PersonaPrompt.Should().StartWith(Chairman.VotingChairmanMarker);
    }

    [Fact]
    public void CouncilBuilder_WithoutVotingChairman_Leaves_VotingStrategy_Null()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .SetChairman("chairman", provider)
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.VotingStrategy.Should().BeNull();
    }

    [Fact]
    public void CouncilBuilder_WithVotingChairman_Null_Throws()
    {
        var act = () => new CouncilBuilder()
            .WithVotingChairman("m", new FakeLLMProvider(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_WithVoting_Runs_Voting_Engine_Without_Crash()
    {
        // The fake reply "1. Option Alpha..." is both the debate response AND the
        // ranking reply — the voting engine extracts option "1. Option Alpha" and
        // parses "1" from the ranking prompt response. A tally may or may not be
        // produced depending on parsing; the test verifies the flow doesn't crash.
        var provider = new FakeLLMProvider(reply: "1. Option Alpha\n2. Option Beta\n3. Option Gamma");
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "Analyst")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithVotingChairman("chairman", provider, new MajorityVotingStrategy())
            .Build();

        var result = await executor.ExecuteAsync();

        // The voting engine ran; the VotingTally field is accessible (may be null
        // if no valid ballots were collected, or populated if parsing succeeded).
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithVoting_And_Ranking_Reply_Populates_Tally()
    {
        // Provider returns numbered options in round 1, and "1,2,3" when asked to rank.
        var provider = new SequenceLLMProvider(
            debateReply: "1. Option Alpha\n2. Option Beta\n3. Option Gamma",
            rankingReply: "1,2,3");
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "Analyst")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithVotingChairman("chairman", provider, new MajorityVotingStrategy())
            .Build();

        var result = await executor.ExecuteAsync();

        result.VotingTally.Should().NotBeNull();
        result.VotingTally!.Method.Should().Be("Majority");
        result.VotingTally.Scores.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DebateResult_ToMarkdown_Includes_VotingTally_Section_When_Present()
    {
        var result = new DebateResult
        {
            StrategyName = "Standard",
            Context = new PromptContext { SystemPrompt = "s", UserPrompt = "q" },
            Participants = ["A"],
            Rounds = [],
            FinalVerdict = "verdict",
            VotingTally = new VotingResult(
                WinningOption: "Option Alpha",
                Score: 3.0,
                Scores: new Dictionary<string, double> { ["Option Alpha"] = 3.0, ["Option Beta"] = 1.0 },
                Method: "Majority")
        };

        var md = result.ToMarkdown();

        md.Should().Contain("🗳️ Voting Tally");
        md.Should().Contain("Option Alpha");
        md.Should().Contain("Majority");
        md.Should().Contain("| Option | Score |");
    }

    [Fact]
    public async Task DebateResult_ToMarkdown_Omits_VotingTally_Section_When_Null()
    {
        var result = new DebateResult
        {
            StrategyName = "Standard",
            Context = new PromptContext { SystemPrompt = "s", UserPrompt = "q" },
            Participants = ["A"],
            Rounds = [],
            FinalVerdict = "verdict"
        };

        var md = result.ToMarkdown();
        md.Should().NotContain("🗳️ Voting Tally");
    }

    [Fact]
    public async Task IVotingStrategy_Custom_Implementation_Can_Be_Used()
    {
        var custom = new CustomVotingStrategy();
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithVotingChairman("chairman", provider, custom)
            .Build();

        executor.VotingStrategy.Should().BeSameAs(custom);
    }

    private static ParticipantBallot MakeBallot(string member, List<(string Name, int Rank)> rankings, double weight = 1.0)
    {
        return new ParticipantBallot(member, weight, rankings.Select(r => new RankedOption(r.Name, r.Rank)).ToList());
    }

    /// <summary>
    ///    Fake provider that returns a different reply for the ranking prompt
    ///    (detected by "Rank ALL options" in the user prompt) vs normal debate rounds.
    /// </summary>
    private sealed class SequenceLLMProvider(string debateReply, string rankingReply) : ILLMProvider
    {
        public string ProviderName => "Sequence";

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(["fake"]);
        public Task<string> ChatAsync(string model, string systemPrompt, string userPrompt, float temperature = 0.7f, CancellationToken ct = default)
        {
            // The voting engine's ranking prompt contains "Rank ALL options".
            if (userPrompt.Contains("Rank ALL options", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(rankingReply);
            return Task.FromResult(debateReply);
        }
        public void Dispose() { }
    }

    private sealed class CustomVotingStrategy : IVotingStrategy
    {
        public string MethodName => "Custom";
        public Task<VotingResult> TallyAsync(IReadOnlyList<ParticipantBallot> ballots, CancellationToken ct = default)
        {
            return Task.FromResult(new VotingResult("custom-winner", 1.0, new Dictionary<string, double>(), MethodName));
        }
    }
}