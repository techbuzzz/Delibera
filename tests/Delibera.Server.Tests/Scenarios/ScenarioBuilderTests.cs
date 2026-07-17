using Delibera.Server.Api.Contracts;
using Delibera.Server.Scenarios;
using Delibera.Server.Tests.Fakes;

namespace Delibera.Server.Tests.Scenarios;

public sealed class ScenarioBuilderTests
{
    private static readonly IConfiguration Config = FakeConfiguration.Default();

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithSingleMember_ReturnsNonNullBuilder()
        => ScenarioBuilder.Build(
               new ScenarioRequest
               {
                   Question = "Should we migrate to microservices?",
                   Members  = [new ScenarioMember { Role = "Architect" }],
               }, Config)
           .Should().NotBeNull();

    [Fact]
    public void Build_WithThreeMembers_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "Best caching strategy?",
                Members  =
                [
                    new ScenarioMember { Role = "Architect", Persona = "Focus on scalability." },
                    new ScenarioMember { Role = "Backend",   Persona = "API developer." },
                    new ScenarioMember { Role = "DBA",       Persona = "DB specialist." },
                ],
            }, Config);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("Critique")]
    [InlineData("Consensus")]
    public void Build_AllDebateStrategies_DoNotThrow(string strategy)
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "Test question",
                Members  = [new ScenarioMember { Role = "A" }],
                Strategy = strategy,
            }, Config);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Majority")]
    [InlineData("BordaCount")]
    [InlineData("Weighted")]
    public void Build_AllVotingStrategies_DoNotThrow(string voting)
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question       = "Vote test",
                Members        = [new ScenarioMember { Role = "A", Weight = 1.0f }],
                VotingStrategy = voting,
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithChairman_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "Architecture decision",
                Members  = [new ScenarioMember { Role = "Lead" }],
                Chairman = new ScenarioChairman
                {
                    SystemPrompt     = "Synthesise into a JSON verdict.",
                    OpeningStatement = false,
                },
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithChairmanOpeningStatement_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "Intro test",
                Members  = [new ScenarioMember { Role = "Lead" }],
                Chairman = new ScenarioChairman { OpeningStatement = true },
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithKnowledgeText_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question      = "Should we adopt Redis?",
                Members       = [new ScenarioMember { Role = "DevLead" }],
                KnowledgeText = "Current stack: .NET 10, PostgreSQL 16.",
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithSystemPrompt_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question     = "DB choice",
                Members      = [new ScenarioMember { Role = "DBA" }],
                SystemPrompt = "You are the Architecture Committee.",
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithMaxRoundsAndTemperature_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question    = "API gateway?",
                Members     = [new ScenarioMember { Role = "Ops" }],
                MaxRounds   = 5,
                Temperature = 0.4f,
            }, Config);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Ollama")]
    [InlineData("OpenAI")]
    [InlineData("AzureOpenAI")]
    [InlineData("Anthropic")]
    [InlineData(null)]
    public void Build_AllProviderTypes_DoNotThrow(string? provider)
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "Provider test",
                Members  = [new ScenarioMember { Role = "R", Provider = provider }],
            }, Config);
        act.Should().NotThrow();
    }

    // ── Validation / edge cases ──────────────────────────────────────────────────

    [Fact]
    public void Build_WithEmptyMembers_ThrowsArgumentException()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest { Question = "No members", Members = [] }, Config);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*at least one member*");
    }

    [Fact]
    public void Build_WeightedVoting_FallsBackToMemberWeights_WhenMapIsNull()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question       = "Weighted fall-back",
                VotingStrategy = "Weighted",
                MemberWeights  = null,
                Members        =
                [
                    new ScenarioMember { Role = "A", Weight = 2.0f },
                    new ScenarioMember { Role = "B", Weight = 1.0f },
                ],
            }, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_UnknownStrategy_FallsBackToStandard_DoesNotThrow()
    {
        var act = () => ScenarioBuilder.Build(
            new ScenarioRequest
            {
                Question = "Unknown strategy",
                Members  = [new ScenarioMember { Role = "A" }],
                Strategy = "totally-unknown",
            }, Config);
        act.Should().NotThrow();
    }
}
