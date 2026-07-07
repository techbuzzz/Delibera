using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;
using Delibera.Server.Templates.Registry;
using Delibera.Server.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delibera.Server.Tests.Services;

public sealed class DebateOrchestrationServiceTests
{
    private static IDebateOrchestrationService BuildService()
    {
        var sp = new ServiceCollection()
            .AddSingleton<ITemplateRegistry, TemplateRegistry>()
            .BuildServiceProvider();

        return new DebateOrchestrationService(
            NullLogger<DebateOrchestrationService>.Instance,
            sp.GetRequiredService<ITemplateRegistry>(),
            sp,
            FakeConfiguration.Default());
    }

    // ── Empty store ────────────────────────────────────────────────────────────

    [Fact]
    public void Find_UnknownId_ReturnsNull()
        => BuildService().Find("does-not-exist").Should().BeNull();

    [Fact]
    public void List_OnEmptyStore_ReturnsEmptyCollection()
        => BuildService().List(null, null, 1, 20).Should().BeEmpty();

    [Fact]
    public void Cancel_UnknownId_ReturnsFalse()
        => BuildService().Cancel("ghost").Should().BeFalse();

    // ── Template path: unknown template ───────────────────────────────────────

    [Fact]
    public async Task RunAsync_UnknownTemplateId_ThrowsInvalidOperation()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RunAsync(
                new CreateDebateRequest { TemplateId = "nonexistent", Question = "?" }, "t1"));
    }

    [Fact]
    public void Enqueue_UnknownTemplateId_ThrowsInvalidOperation()
    {
        var act = () => BuildService().Enqueue(
            new CreateDebateRequest { TemplateId = "nonexistent", Question = "?" }, "t1");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Scenario path: empty members ───────────────────────────────────────────

    [Fact]
    public async Task RunScenarioAsync_NoMembers_ThrowsArgumentException()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.RunScenarioAsync(
                new ScenarioRequest { Question = "Empty?", Members = [] }, "t1"));
    }

    [Fact]
    public void EnqueueScenario_NoMembers_ThrowsArgumentException()
    {
        var act = () => BuildService().EnqueueScenario(
            new ScenarioRequest { Question = "Empty?", Members = [] }, "t1");
        act.Should().Throw<ArgumentException>();
    }

    // ── Scenario path: happy-path record assertions ────────────────────────────

    [Fact]
    public void EnqueueScenario_ValidScenario_RecordIsImmediatelyFindable()
    {
        var svc    = BuildService();
        var record = svc.EnqueueScenario(
            new ScenarioRequest
            {
                Question = "Findable?",
                Members  = [new ScenarioMember { Role = "Reviewer" }],
            }, "t2");

        record.DebateId.Should().NotBeNullOrWhiteSpace();
        svc.Find(record.DebateId).Should().NotBeNull();
    }

    [Fact]
    public void EnqueueScenario_ValidScenario_TemplateIdIsScenario()
    {
        var record = BuildService().EnqueueScenario(
            new ScenarioRequest
            {
                Question = "Template-id test",
                Label    = "my-label",
                Members  = [new ScenarioMember { Role = "X" }],
            }, "t3");

        record.TemplateId.Should().Be("scenario");
        record.Label.Should().Be("my-label");
    }

    [Fact]
    public void EnqueueScenario_WhenLabelIsNull_UsesQuestionAsLabel()
    {
        var record = BuildService().EnqueueScenario(
            new ScenarioRequest
            {
                Question = "Fallback label?",
                Label    = null,
                Members  = [new ScenarioMember { Role = "X" }],
            }, "t4");

        record.Label.Should().Be("Fallback label?");
    }

    [Fact]
    public void List_FilterByScenarioTemplateId_ContainsEnqueuedRecord()
    {
        var svc    = BuildService();
        var record = svc.EnqueueScenario(
            new ScenarioRequest
            {
                Question = "Listed?",
                Members  = [new ScenarioMember { Role = "Y" }],
            }, "t5");

        svc.List("scenario", null, 1, 20)
           .Should().ContainSingle(r => r.DebateId == record.DebateId);
    }

    [Fact]
    public void Cancel_JustEnqueuedRecord_ReturnsTrue()
    {
        var svc    = BuildService();
        var record = svc.EnqueueScenario(
            new ScenarioRequest
            {
                Question = "Cancel me",
                Members  = [new ScenarioMember { Role = "Z" }],
            }, "t6");

        svc.Cancel(record.DebateId).Should().BeTrue();
    }

    [Fact]
    public void Cancel_AlreadyCancelledRecord_ReturnsFalse()
    {
        var svc    = BuildService();
        var record = svc.EnqueueScenario(
            new ScenarioRequest
            {
                Question = "Double cancel",
                Members  = [new ScenarioMember { Role = "W" }],
            }, "t7");

        svc.Cancel(record.DebateId);
        svc.Cancel(record.DebateId).Should().BeFalse();
    }
}
