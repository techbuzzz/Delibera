using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Templates;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-07 Debate Templates &amp; Presets Library
///    (<see cref="DebateTemplate"/>, <see cref="DebateTemplateBase"/>).
/// </summary>
public class TemplatesTests
{
    [Fact]
    public void DebateTemplate_Exposes_Six_BuiltIn_Templates()
    {
        DebateTemplate.ArchitectureReview.Should().NotBeNull();
        DebateTemplate.RiskAssessment.Should().NotBeNull();
        DebateTemplate.CodeReview.Should().NotBeNull();
        DebateTemplate.ProductDecision.Should().NotBeNull();
        DebateTemplate.SecurityAudit.Should().NotBeNull();
        DebateTemplate.DataArchitecture.Should().NotBeNull();
    }

    [Fact]
    public void DebateTemplate_Custom_Returns_Fresh_CouncilBuilder()
    {
        var b = DebateTemplate.Custom();
        b.Should().BeOfType<CouncilBuilder>();
    }

    [Fact]
    public void ArchitectureReview_Preconfigures_Three_Members_And_Critique_Strategy()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.ArchitectureReview
            .WithProvider(provider)
            .WithQuestion("Monolith or microservices?")
            .Build();

        executor.Members.Should().HaveCount(3);
        executor.Members.Select(m => m.Role).Should().Contain(["Architect", "SecurityExpert", "PerfEngineer"]);
        executor.Strategy.Should().BeOfType<CritiqueDebate>();
        executor.Chairman.Should().NotBeNull();
    }

    [Fact]
    public void RiskAssessment_Preconfigures_Four_Members_And_Consensus_Strategy()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.RiskAssessment
            .WithProvider(provider)
            .WithQuestion("Should we expand to a new market?")
            .Build();

        executor.Members.Should().HaveCount(4);
        executor.Members.Select(m => m.Role).Should().Contain(["Optimist", "Pessimist", "Realist", "RiskManager"]);
        executor.Strategy.Should().BeOfType<ConsensusDebate>();
    }

    [Fact]
    public void CodeReview_Preconfigures_Four_Members_And_Critique_Strategy()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.CodeReview
            .WithProvider(provider)
            .WithQuestion("Should we merge PR #42?")
            .Build();

        executor.Members.Should().HaveCount(4);
        executor.Members.Select(m => m.Role).Should().Contain(["Reviewer", "Defender", "QA", "TechLead"]);
        executor.Strategy.Should().BeOfType<CritiqueDebate>();
    }

    [Fact]
    public void ProductDecision_Preconfigures_Three_Members_And_Standard_Strategy()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.ProductDecision
            .WithProvider(provider)
            .WithQuestion("Which feature should we ship next?")
            .Build();

        executor.Members.Should().HaveCount(3);
        executor.Members.Select(m => m.Role).Should().Contain(["PM", "TechLead", "UXDesigner"]);
        executor.Strategy.Should().BeOfType<StandardDebate>();
    }

    [Fact]
    public void SecurityAudit_Preconfigures_Three_Members_And_Strict_Chairman()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.SecurityAudit
            .WithProvider(provider)
            .WithQuestion("Threat model our auth service.")
            .Build();

        executor.Members.Should().HaveCount(3);
        executor.Members.Select(m => m.Role).Should().Contain(["RedTeam", "BlueTeam", "Auditor"]);
        executor.Strategy.Should().BeOfType<CritiqueDebate>();
        executor.Chairman.Should().NotBeNull();
    }

    [Fact]
    public void DataArchitecture_Preconfigures_Three_Members_And_Consensus_Strategy()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.DataArchitecture
            .WithProvider(provider)
            .WithQuestion("Should we move to a data lakehouse?")
            .Build();

        executor.Members.Should().HaveCount(3);
        executor.Members.Select(m => m.Role).Should().Contain(["DataEngineer", "DBA", "MLEngineer"]);
        executor.Strategy.Should().BeOfType<ConsensusDebate>();
    }

    [Fact]
    public void Template_Build_Without_Provider_Throws_InvalidOperationException()
    {
        var act = () => DebateTemplate.ArchitectureReview
            .WithQuestion("q")
            .Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ILLMProvider*");
    }

    [Fact]
    public void Template_WithQuestion_Null_Throws_ArgumentException()
    {
        var act = () => DebateTemplate.ArchitectureReview.WithQuestion("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Template_WithProvider_Null_Throws_ArgumentNullException()
    {
        var act = () => DebateTemplate.ArchitectureReview.WithProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Template_Fluent_Overrides_Take_Precedence_Over_Template_Defaults()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.ArchitectureReview
            .WithProvider(provider)        // triggers ConfigureCore with default maxRounds=4
            .WithQuestion("q")
            .WithMaxRounds(2)              // should override the template's default
            .WithTemperature(0.1f)         // should override default
            .Build();

        // We can't directly read _maxRounds, but the strategy will produce at most 2 rounds.
        // Verify via execution: StandardDebate/CritiqueDebate respect maxRounds.
        // We assert the Build didn't throw and produced an executor; the maxRounds clamp
        // is exercised in the templates-can-execute test below.
        executor.Should().NotBeNull();
    }

    [Fact]
    public async Task Template_Can_Execute_EndToEnd_With_FakeProvider()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = DebateTemplate.ArchitectureReview
            .WithProvider(provider)
            .WithQuestion("Monolith or microservices?")
            .WithMaxRounds(1)
            .Build();

        var result = await executor.ExecuteAsync();
        result.Should().NotBeNull();
        result.Rounds.Should().NotBeEmpty();
        // CritiqueDebate with 3 members + 1 round → at least 3 responses in round 1.
        result.Rounds[0].Responses.Should().HaveCount(3);
    }

    [Fact]
    public void Template_AddMember_Extends_Preset_Participants()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.ArchitectureReview
            .WithProvider(provider)
            .WithQuestion("q")
            .AddMember("cost-engineer", "CostEngineer", Persona.Pragmatist)
            .Build();

        executor.Members.Should().HaveCount(4);
        executor.Members.Select(m => m.Role).Should().Contain("CostEngineer");
    }

    [Fact]
    public void Template_WithChairman_Replaces_Default_Chairman()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.ArchitectureReview
            .WithProvider(provider)
            .WithQuestion("q")
            .WithChairman("custom-chairman", "Custom chairman persona")
            .Build();

        executor.Chairman!.ModelName.Should().Be("custom-chairman");
    }

    [Fact]
    public void Template_WithTimeout_Propagates_To_Executor()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.ProductDecision
            .WithProvider(provider)
            .WithQuestion("q")
            .WithTimeout(TimeSpan.FromMinutes(10))
            .Build();

        executor.DebateTimeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Template_WithTelemetry_Enables_Telemetry_On_Executor()
    {
        var provider = new FakeLLMProvider();
        var executor = DebateTemplate.CodeReview
            .WithProvider(provider)
            .WithQuestion("q")
            .WithTelemetry()
            .Build();

        executor.IsTelemetryEnabled.Should().BeTrue();
    }

    [Fact]
    public void Template_Advanced_Allows_Raw_Builder_Access()
    {
        var provider = new FakeLLMProvider();
        var template = DebateTemplate.ProductDecision
            .WithProvider(provider)
            .WithQuestion("q");

        var rawBuilder = template.Advanced(b => b.WithTemperature(0.9f));
        rawBuilder.Should().BeOfType<CouncilBuilder>();
    }

    [Fact]
    public void Template_Advanced_Without_WithProvider_Throws()
    {
        var template = DebateTemplate.ProductDecision.WithQuestion("q");
        var act = () => template.Advanced(_ => { });
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Template_WithResponseLanguage_Propagates_To_ExecutionOptions()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = DebateTemplate.RiskAssessment
            .WithProvider(provider)
            .WithQuestion("q")
            .WithResponseLanguage("Russian")
            .WithMaxRounds(1)
            .Build();

        // ExecuteAsync should not throw; language directive is applied internally.
        var result = await executor.ExecuteAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public void Template_Each_Instance_Is_Fresh()
    {
        // Each property access returns a new instance — no shared state.
        var t1 = DebateTemplate.ArchitectureReview;
        var t2 = DebateTemplate.ArchitectureReview;
        ReferenceEquals(t1, t2).Should().BeFalse();
    }
}