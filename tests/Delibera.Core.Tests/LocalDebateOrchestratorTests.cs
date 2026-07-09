using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Delibera.Core.Tests;

public sealed class LocalDebateOrchestratorTests
{
    private readonly LocalDebateOrchestrator _orchestrator = new();

    [Fact]
    public async Task EnqueueAsync_ReturnsRunningHandle()
    {
        var builder = CreateBuilder();
        var handle = await _orchestrator.EnqueueAsync("test-1", builder);
        handle.Status.Should().Be(DebateOrchestrationStatus.Running);
        handle.DebateId.Should().Be("test-1");
    }

    [Fact]
    public async Task EnqueueAsync_DuplicateId_Throws()
    {
        var builder = CreateBuilder();
        await _orchestrator.EnqueueAsync("test-dup", builder);
        var act = async () => await _orchestrator.EnqueueAsync("test-dup", builder);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetStatusAsync_UnknownId_ReturnsNull()
    {
        var status = await _orchestrator.GetStatusAsync("nonexistent");
        status.Should().BeNull();
    }

    [Fact]
    public async Task CancelAsync_UnknownId_ReturnsFalse()
    {
        var result = await _orchestrator.CancelAsync("nonexistent");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_UnknownId_YieldsNothing()
    {
        var events = new List<DebateRoundEvent>();
        await foreach (var evt in _orchestrator.StreamAsync("nonexistent"))
            events.Add(evt);
        events.Should().BeEmpty();
    }

    private static ICouncilBuilder CreateBuilder()
    {
        return new CouncilBuilder()
            .AddMember("test-model", new FakeLLMProvider(), "Tester")
            .WithUserPrompt("Test question")
            .WithMaxRounds(1);
    }
}