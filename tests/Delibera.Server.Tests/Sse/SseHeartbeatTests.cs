using System.Text;
using System.Threading.Channels;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;
using Delibera.Server.Sse;
using Microsoft.AspNetCore.Http;

namespace Delibera.Server.Tests.Sse;

/// <summary>
///    W4-10 — SSE robustness.
///    <para>
///    An idle debate produced no bytes at all for minutes at a time, and an intermediary
///    proxy is entitled to close a silent connection — which the client cannot tell apart
///    from a dead debate. And when the stream did end, it just stopped: no event said how.
///    </para>
/// </summary>
public sealed class SseHeartbeatTests
{
   /// <summary>Short enough that the heartbeat tests finish in milliseconds.</summary>
   private static readonly TimeSpan FastHeartbeat = TimeSpan.FromMilliseconds(30);

   private static DebateRecord Record() => new()
   {
      DebateId = "d1",
      TemplateId = "scenario",
      TenantId = "t1",
      Status = DebateStatus.Running,
   };

   private static int CountSseEvents(string body, string eventType) =>
      body.Split("event: ").Count(chunk => chunk.StartsWith(eventType + "\n", StringComparison.Ordinal));

   private static async Task<string> WriteAsync(
      DebateRecord record,
      IDebateOrchestrator orchestrator,
      CancellationToken ct = default,
      TimeSpan? heartbeat = null)
   {
      var ctx = new DefaultHttpContext();
      ctx.Response.Body = new MemoryStream();
      await SseDebateStreamWriter.WriteAsync(record, orchestrator, ctx, ct, heartbeat);
      ctx.Response.Body.Position = 0;
      using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8);
      return await reader.ReadToEndAsync();
   }

   private static DebateRound Round(int number) => new()
   {
      RoundNumber = number,
      RoundName = $"Round {number}",
      Responses = new Dictionary<string, string> { ["Analyst"] = "text" },
   };

   [Fact]
   public async Task Stream_Starts_With_A_Reconnect_Hint()
   {
      var body = await WriteAsync(Record(), ChannelOrchestrator.Open(), CancellationToken.None, FastHeartbeat);

      body.Should().Contain(": retry:");
   }

   [Fact]
   public async Task Idle_Stream_Emits_A_KeepAlive_Comment()
   {
      using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
      var body = await WriteAsync(Record(), ChannelOrchestrator.Open(), cts.Token, FastHeartbeat);

      body.Should().Contain(": keep-alive",
         "a silent connection is closed by intermediaries and looks like a dead debate");
   }

   [Fact]
   public async Task KeepAlive_Does_Not_Disturb_Event_Delivery()
   {
      var orchestrator = ChannelOrchestrator.Open();
      orchestrator.Publish("d1", new DebateRoundEvent.RoundCompleted("d1", Round(1)));
      orchestrator.Publish("d1", new DebateRoundEvent.RoundCompleted("d1", Round(2)));
      using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

      var body = await WriteAsync(Record(), orchestrator, cts.Token, FastHeartbeat);

      CountSseEvents(body, "debate-round").Should().Be(2,
         "a heartbeat is an SSE comment, not an event — clients must not count it as one");
   }

   [Fact]
   public async Task Stream_That_Ends_Without_A_Terminal_Event_Says_So()
   {
      // The orchestrator stream completed on its own. The client used to just see the
      // connection stop, with no indication of why.
      var orchestrator = ChannelOrchestrator.Open();
      orchestrator.Publish("d1", new DebateRoundEvent.RoundCompleted("d1", Round(1)));
      orchestrator.Complete();

      var body = await WriteAsync(Record(), orchestrator, CancellationToken.None, FastHeartbeat);

      CountSseEvents(body, "debate-error").Should().Be(1);
      body.Should().Contain("terminal event");
   }

   [Fact]
   public async Task Completed_Debate_Still_Sends_Its_Terminal_Event_And_No_Error()
   {
      var record = Record();
      record.Status = DebateStatus.Completed;
      record.Rounds.Enqueue(Round(1));

      var body = await WriteAsync(record, ChannelOrchestrator.Open(), CancellationToken.None, FastHeartbeat);

      CountSseEvents(body, "debate-completed").Should().Be(1);
      CountSseEvents(body, "debate-error").Should().Be(0);
   }

   [Fact]
   public async Task Cancelled_Client_Does_Not_Produce_A_Second_Terminal_Event()
   {
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      var body = await WriteAsync(Record(), ChannelOrchestrator.Open(), cts.Token, FastHeartbeat);

      CountSseEvents(body, "debate-error").Should().Be(0,
         "the client is gone; writing another event would only throw");
   }

   /// <summary>
   ///    Channel-backed orchestrator. A real channel is used rather than a hand-rolled
   ///    iterator so the test exercises the same shape the production stream has.
   /// </summary>
   private sealed class ChannelOrchestrator : IDebateOrchestrator
   {
      private readonly Channel<DebateRoundEvent> _channel =
         Channel.CreateUnbounded<DebateRoundEvent>();

      public static ChannelOrchestrator Open() => new();

      public void Publish(string debateId, DebateRoundEvent evt) => _channel.Writer.TryWrite(evt);

      public void Complete() => _channel.Writer.TryComplete();

      public async IAsyncEnumerable<DebateRoundEvent> StreamAsync(
         string debateId,
         [System.Runtime.CompilerServices.EnumeratorCancellation]
         CancellationToken ct = default)
      {
         await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
            yield return evt;
      }

      public Task<DebateResult> ExecuteAsync(ICouncilBuilder builder, CancellationToken ct = default)
         => throw new NotSupportedException();

      public Task<DebateHandle> EnqueueAsync(
         string debateId, ICouncilBuilder builder, CancellationToken ct = default)
         => throw new NotSupportedException();

      public ValueTask<DebateHandle?> GetStatusAsync(string debateId, CancellationToken ct = default)
         => ValueTask.FromResult<DebateHandle?>(null);

      public Task<bool> CancelAsync(string debateId, CancellationToken ct = default)
         => throw new NotSupportedException();
   }
}
