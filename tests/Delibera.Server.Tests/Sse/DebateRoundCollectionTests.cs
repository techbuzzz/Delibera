using System.Text;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;
using Delibera.Server.Sse;
using Microsoft.AspNetCore.Http;

namespace Delibera.Server.Tests.Sse;

/// <summary>
///    W1-05 / W1-07 — the round list and the SSE writer.
///    <para>
///    <c>DebateRecord.Rounds</c> was a <c>List&lt;T&gt;</c> appended by the debate background
///    task and enumerated by request threads, and the SSE writer drained it <em>and</em>
///    wrote the same rounds again from the orchestrator stream, which already replays them.
///    </para>
/// </summary>
public sealed class DebateRoundCollectionTests
{
    private static DebateRound Round(int number) => new()
    {
        RoundNumber = number,
        RoundName = $"Round {number}",
        Responses = new Dictionary<string, string> { ["Analyst"] = "text" },
    };

    private static DebateRecord Record(DebateStatus status = DebateStatus.Running) => new()
    {
        DebateId = "d1",
        TemplateId = "scenario",
        TenantId = "t1",
        Status = status,
    };

    // ── W1-05: concurrent append and read ───────────────────────────────────

    [Fact]
    public async Task Rounds_Survive_Concurrent_Writers_And_Readers()
    {
        // The old List<T> throws "Collection was modified" here; a ConcurrentQueue does not.
        var record = Record();
        const int writers = 6;
        const int perWriter = 500;
        var errors = new List<Exception>();
        using var stop = new CancellationTokenSource();

        var readTasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    // Enumerating directly used to be the crash; a snapshot must be safe too.
                    foreach (var _ in record.Rounds) { }
                    _ = record.RoundsSnapshot().Length;
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
                record.Rounds.Enqueue(Round(w * perWriter + i));
        })));

        stop.Cancel();
        await Task.WhenAll(readTasks);

        errors.Should().BeEmpty();
        record.Rounds.Count.Should().Be(writers * perWriter, "no round may be lost");
    }

    [Fact]
    public void RoundsSnapshot_Preserves_Order()
    {
        var record = Record();
        record.AddRounds([Round(1), Round(2), Round(3)]);

        record.RoundsSnapshot().Select(r => r.RoundNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void AddRounds_Appends_After_Existing_Rounds()
    {
        var record = Record();
        record.Rounds.Enqueue(Round(1));
        record.AddRounds([Round(2)]);

        record.RoundsSnapshot().Select(r => r.RoundNumber).Should().Equal(1, 2);
    }

    // ── W1-07: each round reaches the client exactly once ───────────────────

    [Fact]
    public async Task Sse_Delivers_Each_Round_Exactly_Once()
    {
        // record.Rounds already holds the rounds the client missed; StreamAsync replays
        // them too. Draining the record as well sent every earlier round twice.
        var record = Record();
        record.AddRounds([Round(1), Round(2)]);

        var body = await WriteSseAsync(record, ReplayThenComplete(record.DebateId, Round(1), Round(2)));

        CountEvents(body, "debate-round").Should().Be(2);
        CountEvents(body, "debate-completed").Should().Be(1);
    }

    [Fact]
    public async Task Sse_Does_Not_Duplicate_Rounds_That_Arrive_Live()
    {
        var record = Record();
        record.AddRounds([Round(1)]);

        var body = await WriteSseAsync(record, ReplayThenComplete(record.DebateId, Round(1), Round(2)));

        CountEvents(body, "debate-round").Should().Be(2,
            "round 1 once from the replay, round 2 once from the live stream");
    }

    [Fact]
    public async Task Sse_For_A_Completed_Debate_Replays_Each_Round_Once()
    {
        var record = Record(DebateStatus.Completed);
        record.AddRounds([Round(1), Round(2), Round(3)]);
        record.Result = new DebateResult { StrategyName = "Standard", FinalVerdict = "done", Context = new PromptContext(), Participants = ["Analyst"] };

        var body = await WriteSseAsync(record, Empty());

        CountEvents(body, "debate-round").Should().Be(1,
            "the terminal branch sends the rounds as one array, not one event per round");
        CountEvents(body, "debate-completed").Should().Be(1);
    }

    [Fact]
    public async Task Sse_For_A_Failed_Debate_Emits_An_Error_Event()
    {
        var record = Record(DebateStatus.Failed);
        record.ErrorMessage = "provider exploded";

        var body = await WriteSseAsync(record, Empty());

        CountEvents(body, "debate-error").Should().Be(1);
        body.Should().Contain("provider exploded");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<string> WriteSseAsync(
        DebateRecord record,
        IDebateOrchestrator orchestrator)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await SseDebateStreamWriter.WriteAsync(record, orchestrator, ctx, CancellationToken.None);

        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static int CountEvents(string sseBody, string eventType) =>
        sseBody.Split("event: ")
              .Count(chunk => chunk.StartsWith(eventType + "\n", StringComparison.Ordinal));

    private static IDebateOrchestrator ReplayThenComplete(
        string debateId,
        params DebateRound[] rounds)
    {
        var result = new DebateResult { StrategyName = "Standard", FinalVerdict = "done", Context = new PromptContext(), Participants = ["Analyst"] };

        return new StubOrchestrator(Local(debateId, rounds, result));

        static async IAsyncEnumerable<DebateRoundEvent> Local(
            string id,
            DebateRound[] replay,
            DebateResult completed)
        {
            foreach (var round in replay)
            {
                await Task.Yield();
                yield return new DebateRoundEvent.RoundCompleted(id, round);
            }

            await Task.Yield();
            yield return new DebateRoundEvent.DebateCompleted(id, completed);
        }
    }

    private static IDebateOrchestrator Empty() => new StubOrchestrator(EmptyCore());

    private static async IAsyncEnumerable<DebateRoundEvent> EmptyCore()
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>Orchestrator stub: only the stream is exercised by the SSE writer.</summary>
    private sealed class StubOrchestrator(IAsyncEnumerable<DebateRoundEvent> events) : IDebateOrchestrator
    {
        public IAsyncEnumerable<DebateRoundEvent> StreamAsync(
            string debateId, CancellationToken ct = default) => events;

        public Task<DebateResult> ExecuteAsync(ICouncilBuilder builder, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<DebateHandle> EnqueueAsync(
            string debateId, ICouncilBuilder builder, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask<DebateHandle?> GetStatusAsync(
            string debateId, CancellationToken ct = default)
            => ValueTask.FromResult<DebateHandle?>(null);

        public Task<bool> CancelAsync(string debateId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
