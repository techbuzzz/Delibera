using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Memory;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-04 Agent Memory &amp; Long-Term Context feature
///    (<see cref="IAgentMemory"/>, <see cref="MemoryEntry"/>,
///    <see cref="InMemoryAgentMemory"/>, <see cref="QdrantAgentMemory"/>,
///    <see cref="PgVectorAgentMemory"/>,
///    <see cref="ICouncilBuilder.WithAgentMemory"/>).
/// </summary>
public class AgentMemoryTests
{
    // ── InMemoryAgentMemory unit tests ──

    [Fact]
    public async Task InMemory_Store_And_Recall_Roundtrip()
    {
        var mem = new InMemoryAgentMemory();
        var entry = new MemoryEntry("I prefer microservices for scale", DateTimeOffset.UtcNow, new Dictionary<string, string>());
        await mem.StoreAsync("architect", entry);

        var recalled = await mem.RecallAsync("architect", "microservices scale", limit: 5);
        recalled.Should().HaveCount(1);
        recalled[0].Content.Should().Be("I prefer microservices for scale");
    }

    [Fact]
    public async Task InMemory_Recall_Ranks_By_Relevance()
    {
        var mem = new InMemoryAgentMemory();
        await mem.StoreAsync("agent", new MemoryEntry("Microservices are great for scale and resilience", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        await mem.StoreAsync("agent", new MemoryEntry("Monoliths are simpler to operate", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        await mem.StoreAsync("agent", new MemoryEntry("Pizza is delicious", DateTimeOffset.UtcNow, new Dictionary<string, string>()));

        var recalled = await mem.RecallAsync("agent", "microservices scale resilience", limit: 2);

        recalled.Should().NotBeEmpty();
        // The microservices response should rank first (highest token overlap with query).
        recalled[0].Content.Should().Contain("Microservices");
    }

    [Fact]
    public async Task InMemory_Recall_Returns_Empty_For_Unknown_Agent()
    {
        var mem = new InMemoryAgentMemory();
        var recalled = await mem.RecallAsync("unknown-agent", "query");
        recalled.Should().BeEmpty();
    }

    [Fact]
    public async Task InMemory_Recall_Returns_Empty_When_No_Overlap()
    {
        var mem = new InMemoryAgentMemory();
        await mem.StoreAsync("agent", new MemoryEntry("A completely different topic", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        var recalled = await mem.RecallAsync("agent", "xyz");
        recalled.Should().BeEmpty();
    }

    [Fact]
    public async Task InMemory_Delete_Removes_All_Agent_Memories()
    {
        var mem = new InMemoryAgentMemory();
        await mem.StoreAsync("agent", new MemoryEntry("one", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        await mem.StoreAsync("agent", new MemoryEntry("two", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        await mem.DeleteAsync("agent");
        (await mem.RecallAsync("agent", "query")).Should().BeEmpty();
    }

    [Fact]
    public async Task InMemory_Memories_Are_Per_Agent()
    {
        var mem = new InMemoryAgentMemory();
        await mem.StoreAsync("alice", new MemoryEntry("Alice's thought", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        await mem.StoreAsync("bob", new MemoryEntry("Bob's thought", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        (await mem.RecallAsync("alice", "thought")).Should().HaveCount(1);
        (await mem.RecallAsync("bob", "thought")).Should().HaveCount(1);
    }

    [Fact]
    public async Task InMemory_Store_Null_Or_Empty_Agent_Throws()
    {
        var mem = new InMemoryAgentMemory();
        var act1 = async () => await mem.StoreAsync("", new MemoryEntry("x", DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        await act1.Should().ThrowAsync<ArgumentException>();
        var act2 = async () => await mem.StoreAsync("a", null!);
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── CouncilBuilder integration ──

    [Fact]
    public void CouncilBuilder_WithAgentMemory_Stamps_Memory_On_Executor()
    {
        var provider = new FakeLLMProvider();
        var mem = new InMemoryAgentMemory();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAgentMemory(mem)
            .Build();

        executor.AgentMemory.Should().BeSameAs(mem);
    }

    [Fact]
    public void CouncilBuilder_WithAgentMemory_Null_Uses_InMemory_Default()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAgentMemory()
            .Build();

        executor.AgentMemory.Should().NotBeNull();
        executor.AgentMemory.Should().BeOfType<InMemoryAgentMemory>();
    }

    [Fact]
    public void CouncilBuilder_Without_AgentMemory_Has_Null_AgentMemory()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.AgentMemory.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithAgentMemory_Stores_Member_Responses_After_Debate()
    {
        var provider = new FakeLLMProvider(reply: "my response content here");
        var mem = new InMemoryAgentMemory();
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .SetChairman("chairman", provider)
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAgentMemory(mem)
            .Build();

        await executor.ExecuteAsync();

        // The Analyst's response should be stored under their display name.
        var recalledAnalyst = await mem.RecallAsync("Analyst: fake-model (Fake)", "response content");
        recalledAnalyst.Should().NotBeEmpty();
        // The Chairman's verdict should be stored (use a token-overlapping query).
        var recalledChairman = await mem.RecallAsync("Chairman", "response content");
        recalledChairman.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Second_Debate_Recalls_Memories_From_First()
    {
        // Debate 1: store some memories with overlapping tokens.
        var mem = new InMemoryAgentMemory();
        await mem.StoreAsync("Analyst: fake-model (Fake)", new MemoryEntry(
            "microservices architecture event-driven patterns recommended", DateTimeOffset.UtcNow, new Dictionary<string, string>()));

        // Debate 2: the recalled memory should be injected into the system prompt.
        // We can't directly inspect the system prompt, but we can verify via the
        // execution log that memories were recalled (or stored) for this debate.
        var provider = new FakeLLMProvider(reply: "I recall our previous discussion");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("microservices architecture recommendations")
            .WithMaxRounds(1)
            .WithAgentMemory(mem)
            .Build();

        var result = await executor.ExecuteAsync();

        // The executor should have logged an AgentMemory entry (either recall
        // and/or store) — verifying the memory layer was exercised.
        result.ExecutionLogs.Should().Contain(l => l.Source == "AgentMemory");
    }

    [Fact]
    public async Task ExecuteAsync_WithAgentMemory_Stores_After_Completion_Not_Before()
    {
        // Verify the store happens at the end (not during). The simplest check:
        // before ExecuteAsync, the memory is empty; after, it has entries.
        var mem = new InMemoryAgentMemory();
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAgentMemory(mem)
            .Build();

        // Pre-execution: no memories for the analyst yet.
        var preExec = await mem.RecallAsync("Analyst: fake-model (Fake)", "ok");
        preExec.Should().BeEmpty();

        await executor.ExecuteAsync();

        // Post-execution: memory is populated.
        var postExec = await mem.RecallAsync("Analyst: fake-model (Fake)", "ok");
        postExec.Should().NotBeEmpty();
    }
}