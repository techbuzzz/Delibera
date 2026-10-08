using System.Text;
using Delibera.Core.Interfaces;

namespace Delibera.Server.Sse;

/// <summary>
///    Writes debate round events to an HTTP response as Server-Sent Events.
///    Consumes <see cref="IDebateOrchestrator.StreamAsync" /> so that SSE works
///    both in-process (LocalDebateOrchestrator) and in distributed deployments
///    (RedisDebateOrchestrator).
/// </summary>
public static class SseDebateStreamWriter
{
   private static readonly JsonSerializerOptions _json = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
   };

   /// <summary>
   ///    How long the stream may stay silent before a keep-alive comment is sent. A
   ///    parameter of <see cref="WriteAsync" /> rather than a static, so a test can prove a
   ///    heartbeat exists without waiting 15 seconds and without mutating global state that
   ///    other test classes running in parallel would observe.
   /// </summary>
   public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

   private static readonly byte[] CommentPrefix = ": "u8.ToArray();

   private static readonly byte[] EventPrefix = "event: "u8.ToArray();
   private static readonly byte[] DataPrefix = "data: "u8.ToArray();
   private static readonly byte[] NewlineBytes = "\n"u8.ToArray();
   private static readonly byte[] DoubleNewlineBytes = "\n\n"u8.ToArray();

   /// <summary>
   ///    Streams debate events from <see cref="IDebateOrchestrator.StreamAsync" /> as SSE.
   ///    Falls back to <see cref="DebateRecord" /> for legacy in-process debates that
   ///    have already completed before the client connected.
   /// </summary>
   /// <param name="heartbeatInterval">
   ///    How long the stream may stay silent before a keep-alive comment is written.
   ///    Defaults to <see cref="DefaultHeartbeatInterval" />.
   /// </param>
   public static async Task WriteAsync(
      DebateRecord record,
      IDebateOrchestrator orchestrator,
      HttpContext ctx,
      CancellationToken ct,
      TimeSpan? heartbeatInterval = null)
   {
      var heartbeat = heartbeatInterval ?? DefaultHeartbeatInterval;
      ctx.Response.Headers.ContentType = "text/event-stream";
      ctx.Response.Headers.CacheControl = "no-cache";
      ctx.Response.Headers["X-Accel-Buffering"] = "no";

      // If already terminal, write the result and close immediately.
      if (record.Status is DebateStatus.Completed)
      {
         await WriteSseEventAsync(ctx, "debate-round", record.RoundsSnapshot().Select(r => r.ToDto()), ct);
         await WriteSseEventAsync(ctx, "debate-completed", BuildCompletedPayload(record), ct);
         return;
      }

      if (record.Status is DebateStatus.Failed)
      {
         await WriteSseEventAsync(ctx, "debate-error", BuildErrorPayload(record), ct);
         return;
      }

      if (record.Status is DebateStatus.Cancelled)
      {
         await WriteSseEventAsync(ctx, "debate-cancelled", new { record.DebateId }, ct);
         return;
      }

      // Single source of truth for rounds: the orchestrator stream. It already replays the
      // rounds that completed before the client connected and then yields the live ones, so
      // draining record.Rounds here as well delivered every earlier round twice. Recording
      // into record.Rounds from this loop was a second writer to a collection the debate
      // background task also appends to; DebateOrchestrationService already records them.
      var terminalWritten = false;
      IAsyncEnumerator<DebateRoundEvent>? enumerator = null;
      Task<bool>? pendingMove = null;
      try
      {
         // A comment line every heartbeat interval. An idle debate produces no events for
         // minutes at a time, and an intermediary proxy is entitled to close a connection
         // that has been silent — which looked to the client exactly like a dead debate.
         await WriteSseCommentAsync(ctx, "retry: 3000", ct);

         enumerator = orchestrator
            .StreamAsync(record.DebateId, ct)
            .GetAsyncEnumerator(ct);

         // Exactly one MoveNextAsync per iteration, started once and then reused by every
         // heartbeat pulse. Two separate defects lived in this loop, and both of them were
         // invisible until the SSE tests could actually run:
         //
         //  1. ValueTask.AsTask() on a compiler-generated async iterator returns a
         //     different Task instance on the second call, so `winner != next.AsTask()`
         //     was true even when the move had won the race. Every genuine event was
         //     misread as a heartbeat and silently skipped — a debate streamed zero rounds.
         //     The winner is now compared against the single hoisted Task by reference.
         //
         //  2. After a heartbeat pulse the loop used to `continue`, which called
         //     MoveNextAsync again while the previous move was still pending, and it left
         //     that move in flight when the method returned on a terminal event. An async
         //     iterator forbids a second concurrent MoveNextAsync and forbids DisposeAsync
         //     while one is in flight, so a debate that completed mid-stream raised
         //     NotSupportedException *after* its terminal event had already been written.
         //     See ReleaseStreamAsync for the disposal half of the fix.
         pendingMove = enumerator.MoveNextAsync().AsTask();

         while (!ct.IsCancellationRequested)
         {
            bool hasNext;
            using (var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
               var pulse = Task.Delay(heartbeat, heartbeatCts.Token);

               var winner = await Task.WhenAny(pendingMove, pulse).ConfigureAwait(false);
               if (!ReferenceEquals(winner, pendingMove))
               {
                  // Nothing arrived in time. Keep the connection warm and wait for the same
                  // pending MoveNextAsync — it is not cancelled or abandoned, and it must
                  // not be re-issued: the iterator rejects a concurrent move.
                  heartbeatCts.Cancel();
                  await WriteSseCommentAsync(ctx, "keep-alive", ct);
                  continue;
               }

               heartbeatCts.Cancel();
               hasNext = await pendingMove.ConfigureAwait(false);
            }

            if (!hasNext)
               break; // the stream completed without a terminal event

            switch (enumerator.Current)
            {
               case DebateRoundEvent.RoundCompleted rc:
                  await WriteSseEventAsync(ctx, "debate-round", rc.Round.ToDto(), ct);
                  break;
               case DebateRoundEvent.DebateCompleted dc:
                  record.Result = dc.Result;
                  record.Status = DebateStatus.Completed;
                  record.CompletedAt = dc.Result?.CompletedAt ?? DateTimeOffset.UtcNow;
                  await WriteSseEventAsync(ctx, "debate-completed", BuildCompletedPayload(record), ct);
                  terminalWritten = true;
                  return;
               case DebateRoundEvent.DebateFailed df:
                  record.ErrorMessage = df.Error;
                  record.Status = DebateStatus.Failed;
                  record.CompletedAt = DateTimeOffset.UtcNow;
                  await WriteSseEventAsync(ctx, "debate-error", BuildErrorPayload(record), ct);
                  terminalWritten = true;
                  return;
               case DebateRoundEvent.DebateCancelled:
                  record.Status = DebateStatus.Cancelled;
                  record.CompletedAt = DateTimeOffset.UtcNow;
                  await WriteSseEventAsync(ctx, "debate-cancelled", new { record.DebateId }, ct);
                  terminalWritten = true;
                  return;
            }

            // Only once the current event has been written, and never while a move is in
            // flight — pendingMove has just been awaited to completion above.
            pendingMove = enumerator.MoveNextAsync().AsTask();
         }
      }
      catch (OperationCanceledException)
      {
         // The client went away; there is nobody left to tell.
      }
      finally
      {
         // Before anything else: the move has to land and the iterator has to be disposed.
         // Skipping this is what surfaced NotSupportedException to the caller.
         if (enumerator is not null)
            await ReleaseStreamAsync(enumerator, pendingMove).ConfigureAwait(false);

         // A client can otherwise just see the stream stop. Say how it ended, unless the
         // response has already been torn down.
         if (!terminalWritten && !ctx.Response.HasStarted)
            try
            {
               record.ErrorMessage ??= ct.IsCancellationRequested
                  ? "The client closed the stream before the debate finished."
                  : "The event stream ended before a terminal event was received.";
               await WriteSseEventAsync(ctx, "debate-error", BuildErrorPayload(record), ct);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
               // The connection is gone; nothing left to report to.
            }
      }
   }

   // ── Private ─────────────────────────────────────────────────────────────────

   /// <summary>
   ///    Releases the orchestrator stream.
   ///    <para>
   ///       A compiler-generated async iterator refuses <c>DisposeAsync</c> while a
   ///       <c>MoveNextAsync</c> is still in flight — it throws
   ///       <see cref="NotSupportedException" /> and the response dies with it. The pending
   ///       move is therefore awaited to completion first. Every orchestrator in the box
   ///       takes an <c>[EnumeratorCancellation]</c> token and passes it down to its channel
   ///       or Redis read, so a client disconnect settles the move and this returns promptly.
   ///    </para>
   ///    <para>
   ///       Anything the abandoned move raised is observed and dropped here: the caller
   ///       already has its own exception to deal with, and surfacing a second one from a
   ///       cleanup path would only hide it.
   ///    </para>
   /// </summary>
   private static async Task ReleaseStreamAsync(
      IAsyncEnumerator<DebateRoundEvent> enumerator,
      Task<bool>? pendingMove)
   {
      if (pendingMove is not null)
         try
         {
            await pendingMove.ConfigureAwait(false);
         }
         catch
         {
            // The move is being abandoned, not consumed. Whoever wanted its outcome has
            // already been told; rethrowing from cleanup would mask the real failure.
         }

      await enumerator.DisposeAsync().ConfigureAwait(false);
   }

   /// <summary>
   ///    Writes a bare SSE comment line. Comments are ignored by event listeners but keep
   ///    the connection — and any proxy in front of it — alive.
   /// </summary>
   private static async Task WriteSseCommentAsync(
      HttpContext ctx,
      string comment,
      CancellationToken ct)
   {
      var bytes = Encoding.UTF8.GetBytes(comment + "\n\n");
      await ctx.Response.Body.WriteAsync(CommentPrefix, ct);
      await ctx.Response.Body.WriteAsync(bytes, ct);
      await ctx.Response.Body.FlushAsync(ct);
   }

   private static async Task WriteSseEventAsync<T>(
      HttpContext ctx,
      string eventType,
      T payload,
      CancellationToken ct)
   {
      var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _json);
      await ctx.Response.Body.WriteAsync(EventPrefix, ct);
      await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(eventType), ct);
      await ctx.Response.Body.WriteAsync(NewlineBytes, ct);
      await ctx.Response.Body.WriteAsync(DataPrefix, ct);
      await ctx.Response.Body.WriteAsync(bytes, ct);
      await ctx.Response.Body.WriteAsync(DoubleNewlineBytes, ct);
      await ctx.Response.Body.FlushAsync(ct);
   }

   private static object BuildCompletedPayload(DebateRecord record)
   {
      return new
      {
         debateId = record.DebateId,
         status = record.Status.ToString(),
         verdict = record.Result?.FinalVerdict,
         completedAt = record.CompletedAt,
         error = record.ErrorMessage
      };
   }

   private static object BuildErrorPayload(DebateRecord record)
   {
      return new
      {
         debateId = record.DebateId,
         status = record.Status.ToString(),
         error = record.ErrorMessage
      };
   }
}
