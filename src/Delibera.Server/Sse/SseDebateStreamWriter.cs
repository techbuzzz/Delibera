using System.Text;
using System.Text.Json;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Server.Api.Mapping;
using Delibera.Server.Services;

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
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
   };

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
      try
      {
         // A comment line every heartbeat interval. An idle debate produces no events for
         // minutes at a time, and an intermediary proxy is entitled to close a connection
         // that has been silent — which looked to the client exactly like a dead debate.
         await WriteSseCommentAsync(ctx, "retry: 3000", ct);

         await using var enumerator = orchestrator
            .StreamAsync(record.DebateId, ct)
            .GetAsyncEnumerator(ct);

         while (!ct.IsCancellationRequested)
         {
            var next = enumerator.MoveNextAsync();
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pulse = Task.Delay(heartbeat, heartbeatCts.Token);

            var winner = await Task.WhenAny(next.AsTask(), pulse).ConfigureAwait(false);
            if (winner != next.AsTask())
            {
               // Nothing arrived in time. Keep the connection warm and wait for the same
               // pending MoveNextAsync — it is not cancelled or abandoned.
               heartbeatCts.Cancel();
               await WriteSseCommentAsync(ctx, "keep-alive", ct);
               continue;
            }

            heartbeatCts.Cancel();
            if (!next.Result)
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
         }
      }
      catch (OperationCanceledException)
      {
         // The client went away; there is nobody left to tell.
      }
      finally
      {
         // A client can otherwise just see the stream stop. Say how it ended, unless the
         // response has already been torn down.
         if (!terminalWritten && !ctx.Response.HasStarted)
         {
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
   }

   // ── Private ────────────────────────────────────────────────────────────────

   /// <summary>
   ///    How long the stream may stay silent before a keep-alive comment is sent. A
   ///    parameter of <see cref="WriteAsync" /> rather than a static, so a test can prove a
   ///    heartbeat exists without waiting 15 seconds and without mutating global state that
   ///    other test classes running in parallel would observe.
   /// </summary>
   public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

   private static readonly byte[] CommentPrefix = ": "u8.ToArray();

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

   private static readonly byte[] EventPrefix = "event: "u8.ToArray();
   private static readonly byte[] DataPrefix = "data: "u8.ToArray();
   private static readonly byte[] NewlineBytes = "\n"u8.ToArray();
   private static readonly byte[] DoubleNewlineBytes = "\n\n"u8.ToArray();

   private static object BuildCompletedPayload(DebateRecord record) => new
   {
      debateId = record.DebateId,
      status = record.Status.ToString(),
      verdict = record.Result?.FinalVerdict,
      completedAt = record.CompletedAt,
      error = record.ErrorMessage
   };

   private static object BuildErrorPayload(DebateRecord record) => new
   {
      debateId = record.DebateId,
      status = record.Status.ToString(),
      error = record.ErrorMessage
   };
}
