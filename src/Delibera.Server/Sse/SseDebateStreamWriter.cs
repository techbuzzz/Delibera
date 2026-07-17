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
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    ///    Streams debate events from <see cref="IDebateOrchestrator.StreamAsync" /> as SSE.
    ///    Falls back to <see cref="DebateRecord" /> for legacy in-process debates that
    ///    have already completed before the client connected.
    /// </summary>
    public static async Task WriteAsync(
        DebateRecord record,
        IDebateOrchestrator orchestrator,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.Headers.ContentType  = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        // If already terminal, write the result and close immediately.
        if (record.Status is DebateStatus.Completed)
        {
            await WriteSseEventAsync(ctx, "debate-round", record.Rounds.Select(r => r.ToDto()), ct);
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

        // Drain rounds that completed before the client connected.
        foreach (var round in record.Rounds)
            await WriteSseEventAsync(ctx, "debate-round", round.ToDto(), ct);

        // Stream live rounds from the orchestrator.
        await foreach (var evt in orchestrator.StreamAsync(record.DebateId, ct))
        {
            switch (evt)
            {
                case DebateRoundEvent.RoundCompleted rc:
                    record.Rounds.Add(rc.Round);
                    await WriteSseEventAsync(ctx, "debate-round", rc.Round.ToDto(), ct);
                    break;
                case DebateRoundEvent.DebateCompleted dc:
                    record.Result = dc.Result;
                    record.Status = DebateStatus.Completed;
                    record.CompletedAt = dc.Result?.CompletedAt ?? DateTimeOffset.UtcNow;
                    await WriteSseEventAsync(ctx, "debate-completed", BuildCompletedPayload(record), ct);
                    return;
                case DebateRoundEvent.DebateFailed df:
                    record.ErrorMessage = df.Error;
                    record.Status = DebateStatus.Failed;
                    record.CompletedAt = DateTimeOffset.UtcNow;
                    await WriteSseEventAsync(ctx, "debate-error", BuildErrorPayload(record), ct);
                    return;
                case DebateRoundEvent.DebateCancelled:
                    record.Status = DebateStatus.Cancelled;
                    record.CompletedAt = DateTimeOffset.UtcNow;
                    await WriteSseEventAsync(ctx, "debate-cancelled", new { record.DebateId }, ct);
                    return;
            }
        }
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private static async Task WriteSseEventAsync<T>(
        HttpContext ctx,
        string eventType,
        T payload,
        CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(payload, _json);
        await ctx.Response.WriteAsync($"event: {eventType}\n", ct);
        await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    private static object BuildCompletedPayload(DebateRecord record) => new
    {
        debateId    = record.DebateId,
        status      = record.Status.ToString(),
        verdict     = record.Result?.FinalVerdict,
        completedAt = record.CompletedAt,
        error       = record.ErrorMessage
    };

    private static object BuildErrorPayload(DebateRecord record) => new
    {
        debateId = record.DebateId,
        status   = record.Status.ToString(),
        error    = record.ErrorMessage
    };
}