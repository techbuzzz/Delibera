using Delibera.Core.Council;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Interfaces;
using Delibera.Core.Persistence;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    W2-03 — checkpointing used to re-resolve the target checkpoint on every round.
///    <para>
///    With no resume id, each round ran <c>store.ListAsync()</c> and linearly scanned the
///    result for a checkpoint with the same question. With a resume id it loaded that
///    checkpoint per round. Either way a 10-round debate performed 10 full store reads to
///    discover an identifier that cannot change, and each read grows with the number of
///    stored debates (every one of which carries all of its rounds).
///    </para>
/// </summary>
public sealed class CheckpointResolutionTests
{
    private static ICouncilBuilder Builder(IDebateStore store, int maxRounds = 3) =>
        new CouncilBuilder()
            .AddMember("test-model", new FakeLLMProvider(), "Tester")
            .WithUserPrompt("Checkpoint question")
            .WithMaxRounds(maxRounds)
            .WithPersistence(store);

    [Fact]
    public async Task Fresh_Debate_Resolves_The_Store_Only_Once()
    {
        var store = new CountingStore();
        var executor = Builder(store).Build();

        await executor.ExecuteAsync();

        store.SaveCallCount.Should().BeGreaterThan(1, "a checkpoint is written per round");
        store.ListCallCount.Should().Be(1,
            "the id cannot change after the first save, so the store must be scanned once");
    }

    [Fact]
    public async Task Resumed_Debate_Loads_The_Resume_Id_Only_Once()
    {
        var inner = new InMemoryDebateStore();
        var seedId = await inner.SaveCheckpointAsync(
            new DebateCheckpoint(
                string.Empty,
                DateTimeOffset.UtcNow,
                1,
                [],
                new CouncilOptions { MaxRounds = 1 },
                "Checkpoint question"));
        var store = new CountingStore(inner);

        var executor = new CouncilBuilder()
            .AddMember("test-model", new FakeLLMProvider(), "Tester")
            .WithUserPrompt("Checkpoint question")
            .WithMaxRounds(2)
            .WithPersistence(store)
            .ResumeFrom(seedId)
            .Build();

        await executor.ExecuteAsync();

        store.LoadCallCount.Should().Be(1);
        store.ListCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Every_Round_Overwrites_The_Same_Checkpoint()
    {
        // The behaviour the removed per-round scan used to provide: successive saves land
        // on one record, not on a new one per round.
        var store = new CountingStore();
        var executor = Builder(store, maxRounds: 3).Build();

        await executor.ExecuteAsync();

        var ids = store.SavedDebateIds.Distinct().ToArray();
        store.SaveCallCount.Should().BeGreaterThan(1);
        ids.Should().HaveCount(1, "all rounds must write to the same checkpoint id");
        ids[0].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_New_Debate_Does_Not_Inherit_A_Previous_Checkpoints_Id()
    {
        var store = new CountingStore();

        var first = Builder(store).Build();
        await first.ExecuteAsync();

        var second = new CouncilBuilder()
            .AddMember("test-model", new FakeLLMProvider(), "Tester")
            .WithUserPrompt("A completely different question")
            .WithMaxRounds(1)
            .WithPersistence(store)
            .Build();
        await second.ExecuteAsync();

        store.SavedDebateIds.Distinct().Should().HaveCount(2);
    }

    /// <summary>Counts store traffic while delegating to an in-memory store.</summary>
    private sealed class CountingStore(IDebateStore? inner = null) : IDebateStore
    {
        private readonly IDebateStore _inner = inner ?? new InMemoryDebateStore();
        private readonly List<string> _savedIds = [];
        private readonly Lock _gate = new();

        public int SaveCallCount { get; private set; }

        public int ListCallCount { get; private set; }

        public int LoadCallCount { get; private set; }

        public IReadOnlyList<string> SavedDebateIds
        {
            get
            {
                lock (_gate) return _savedIds.ToArray();
            }
        }

        public async ValueTask<string> SaveCheckpointAsync(
            DebateCheckpoint checkpoint, CancellationToken ct = default)
        {
            lock (_gate)
            {
                SaveCallCount++;
                _savedIds.Add(checkpoint.DebateId);
            }

            var id = await _inner.SaveCheckpointAsync(checkpoint, ct);
            lock (_gate) _savedIds[^1] = id;
            return id;
        }

        public ValueTask<DebateCheckpoint?> LoadCheckpointAsync(
            string debateId, CancellationToken ct = default)
        {
            lock (_gate) LoadCallCount++;
            return _inner.LoadCheckpointAsync(debateId, ct);
        }

        public ValueTask<IReadOnlyList<DebateCheckpointMeta>> ListAsync(CancellationToken ct = default)
        {
            lock (_gate) ListCallCount++;
            return _inner.ListAsync(ct);
        }

        public ValueTask DeleteAsync(string debateId, CancellationToken ct = default)
            => _inner.DeleteAsync(debateId, ct);
    }
}
