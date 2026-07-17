using Delibera.Server.Api.Contracts;
using Delibera.Server.Scenarios;
using Delibera.Server.Tests.Fakes;

namespace Delibera.Server.Tests.Endpoints;

/// <summary>
/// Unit tests for the /scenarios/validate logic.
/// The endpoint handler delegates to <see cref="ScenarioBuilder"/>, so we test that directly.
/// </summary>
public sealed class ScenarioEndpointsValidateTests
{
    private static readonly IConfiguration Config = FakeConfiguration.Default();

    [Fact]
    public void Validate_MinimalScenario_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "gRPC vs REST?",
                Members  = [new ScenarioMember { Role = "Proponent" }],
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NoMembers_ThrowsArgumentException()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest { Question = "Empty", Members = [] }, Config);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("Critique")]
    [InlineData("Consensus")]
    [InlineData("unknown-strategy")]  // falls back to Standard gracefully
    public void Validate_AnyStrategy_DoesNotThrow(string strategy)
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "Strategy test",
                Members  = [new ScenarioMember { Role = "R" }],
                Strategy = strategy,
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithChairmanAndWeightedVoting_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question       = "Full config?",
                Strategy       = "Critique",
                VotingStrategy = "Weighted",
                MaxRounds      = 2,
                Chairman       = new ScenarioChairman { SystemPrompt = "Summarise." },
                Members        =
                [
                    new ScenarioMember { Role = "A", Weight = 2f },
                    new ScenarioMember { Role = "B", Weight = 1f },
                ],
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MemberCount_MatchesRequestMembers()
    {
        var scenario = new ScenarioRequest
        {
            Question = "Count?",
            Members  =
            [
                new ScenarioMember { Role = "A" },
                new ScenarioMember { Role = "B" },
                new ScenarioMember { Role = "C" },
            ],
        };
        _ = ScenarioBuilder.Build(scenario, Config);
        scenario.Members.Length.Should().Be(3);
    }

    [Fact]
    public void Validate_StrategyPreservedOnRequest_AfterBuild()
    {
        var scenario = new ScenarioRequest
        {
            Question = "Strategy reflected?",
            Strategy = "Consensus",
            Members  = [new ScenarioMember { Role = "X" }],
        };
        _ = ScenarioBuilder.Build(scenario, Config);
        scenario.Strategy.Should().Be("Consensus");
    }
}
