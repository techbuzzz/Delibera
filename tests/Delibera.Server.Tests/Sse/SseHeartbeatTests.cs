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
      // The stream has to be given a bounded lifetime: this orchestrator never completes
      // its channel, so a correct writer keeps the connection open until the client goes
      // away. It only used to return because the writer faulted mid-pump.
      using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
      var body = await WriteAsync(Record(), ChannelOrchestrator.Open(), cts.Token, FastHeartbeat);

      body.Should().StartWith(": retry:", "the reconnect hint is the first thing on the wire");
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
   ///    A buffering proxy once passed every other test in this file while delivering each
   ///    round 150–200 seconds late. The other tests here cannot see that failure: they hand
   ///    the writer a <see cref="MemoryStream" /> and read everything back after the debate is
   ///    over, so a frame held in a buffer and a frame written promptly look identical.
   ///    <para>
   ///       This one watches the wire instead. The terminal event is withheld, so the debate
   ///       cannot finish on its own; the round must arrive anyway, within the timeout. If it
   ///       only shows up when the writer returns, the frame was sitting in a buffer — which is
   ///       precisely the defect being guarded against.
   ///    </para>
   /// </summary>
   [Fact]
   public async Task Round_Is_Flushed_While_The_Debate_Is_Still_Running()
   {
      var orchestrator = ChannelOrchestrator.Open();
      orchestrator.Publish("d1", new DebateRoundEvent.RoundCompleted("d1", Round(1)));

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      var ctx = new DefaultHttpContext();
      var wire = new SignalStream();
      ctx.Response.Body = wire;

      // This never returns on its own: the orchestrator channel is left open and no terminal
      // event is published, so the write task is still running when the round must arrive.
      var write = SseDebateStreamWriter.WriteAsync(Record(), orchestrator, ctx, cts.Token, FastHeartbeat);

      var arrived = await Task.WhenAny(wire.SawRound, Task.Delay(TimeSpan.FromSeconds(3)));

      arrived.Should().BeSameAs(wire.SawRound,
         "a round that only reaches the client when the stream ends is indistinguishable from "
         + "buffering, which is how this failure shipped in the first place");

      await cts.CancelAsync();
      await write;
   }

   /// <summary>
   ///    Write-only stream that completes a task the first time a round frame lands on it.
   /// </summary>
   private sealed class SignalStream : Stream
   {
      private readonly TaskCompletionSource _sawRound =
         new(TaskCreationOptions.RunContinuationsAsynchronously);
      private readonly StringBuilder _received = new();

      public Task SawRound => _sawRound.Task;

      public override bool CanRead => false;
      public override bool CanSeek => false;
      public override bool CanWrite => true;
      public override long Length => 0;
      public override long Position { get => 0; set => throw new NotSupportedException(); }

      public override void Write(byte[] buffer, int offset, int count) =>
         Observe(buffer.AsSpan(offset, count));

      public override void Write(ReadOnlySpan<byte> buffer) => Observe(buffer);

      public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
      {
         Observe(buffer.AsSpan(offset, count));
         return Task.CompletedTask;
      }

      public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
      {
         Observe(buffer.Span);
         return ValueTask.CompletedTask;
      }

      private void Observe(ReadOnlySpan<byte> chunk)
      {
         var text = Encoding.UTF8.GetString(chunk);
         lock (_received)
         {
            _received.Append(text);
            if (text.Contains("event: debate-round", StringComparison.Ordinal))
               _sawRound.TrySetResult();
         }
      }

      public override void Flush() { }
      public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
      public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
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
