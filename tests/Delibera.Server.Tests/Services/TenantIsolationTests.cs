using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;
using Delibera.Server.Templates.Registry;
using Delibera.Server.Tests.Fakes;
using Delibera.Core.Interfaces;
using Delibera.Core.Council;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delibera.Server.Tests.Services;

/// <summary>
///    W1-04 — tenant isolation.
///    <para>
///    <c>DebateRecord</c> carried a <c>TenantId</c> and the middleware resolved the caller,
///    but no lookup ever compared the two: any request could read, list, cancel or export
///    another tenant's debate by id. The tenant is now a required parameter of every
///    lookup, so the compiler rejects a call site that forgets it.
///    </para>
/// </summary>
public sealed class TenantIsolationTests
{
    private static IDebateOrchestrationService BuildService() =>
        new DebateOrchestrationService(
            NullLogger<DebateOrchestrationService>.Instance,
            new ServiceCollection()
                .AddSingleton<ITemplateRegistry, TemplateRegistry>()
                .BuildServiceProvider()
                .GetRequiredService<ITemplateRegistry>(),
            new ServiceCollection().BuildServiceProvider(),
            FakeConfiguration.Default(),
            new LocalDebateOrchestrator());

    private static DebateRecord Seed(IDebateOrchestrationService svc, string tenantId, string question) =>
        svc.EnqueueScenario(
            new ScenarioRequest
            {
                Question = question,
                Members = [new ScenarioMember { Role = "Reviewer" }],
            },
            tenantId);

    [Fact]
    public void Find_WithTheOwningTenant_ReturnsTheRecord()
    {
        var svc = BuildService();
        var record = Seed(svc, "acme", "mine?");

        svc.Find(record.DebateId, "acme").Should().NotBeNull();
    }

    [Fact]
    public void Find_WithAForeignTenant_ReturnsNull()
    {
        var svc = BuildService();
        var record = Seed(svc, "acme", "secret?");

        svc.Find(record.DebateId, "globex").Should().BeNull();
    }

    [Fact]
    public void Find_ComparesTheTenantExactly()
    {
        // Ordinal comparison: "Acme" and "acme" are different tenants, so a case-folded
        // comparison would silently widen access instead of narrowing it.
        var svc = BuildService();
        var record = Seed(svc, "Acme", "case matters?");

        svc.Find(record.DebateId, "acme").Should().BeNull();
        svc.Find(record.DebateId, "Acme").Should().NotBeNull();
    }

    [Fact]
    public void List_ReturnsOnlyTheCallersDebates()
    {
        var svc = BuildService();
        Seed(svc, "acme", "a1");
        Seed(svc, "acme", "a2");
        Seed(svc, "globex", "g1");

        var mine = svc.List("acme", null, null, 1, 20);

        mine.Should().HaveCount(2);
        mine.Should().OnlyContain(r => r.TenantId == "acme");
    }

    [Fact]
    public void List_FiltersByTenantBeforePaginating()
    {
        // The tempting implementation — take a page of everything, then filter — returns
        // an empty or shifted page once another tenant owns more records. This test fails
        // against that variant.
        var svc = BuildService();
        Seed(svc, "acme", "a1");
        Seed(svc, "acme", "a2");
        Seed(svc, "acme", "a3");
        Seed(svc, "globex", "g1");

        svc.List("acme", null, null, 1, 2).Should().HaveCount(2);
        svc.List("acme", null, null, 2, 2).Should().HaveCount(1);
        svc.List("globex", null, null, 1, 2).Should().ContainSingle();
    }

    [Fact]
    public void Cancel_WithAForeignTenant_ReturnsFalseAndLeavesTheDebateRunning()
    {
        var svc = BuildService();
        var record = Seed(svc, "acme", "do not cancel");

        svc.Cancel(record.DebateId, "globex").Should().BeFalse();
        record.Status.Should().NotBe(DebateStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WithTheOwningTenant_Succeeds()
    {
        var svc = BuildService();
        var record = Seed(svc, "acme", "cancel me");

        svc.Cancel(record.DebateId, "acme").Should().BeTrue();
        record.Status.Should().Be(DebateStatus.Cancelled);
    }
}
