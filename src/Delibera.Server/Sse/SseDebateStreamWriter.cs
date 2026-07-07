using System.Text.Json;
using Delibera.Core.Models;
using Delibera.Server.Api.Mapping;
using Delibera.Server.Services;

namespace Delibera.Server.Sse;

/// <summary>
/// Writes debate rounds to an HTTP response as a Server-Sent Events stream.
/// Uses the <see cref="DebateRecord"/> channel to receive rounds live while
/// the debate is still running, then drains any already-completed rounds first.
/// </summary>
public static class SseDebateStreamWriter
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(
        DebateRecord      record,
        HttpContext        ctx,
        CancellationToken ct)
    {
        ctx.Response.Headers.ContentType  = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering

        // Drain rounds that completed before the client connected
        foreach (var round in record.Rounds)
        {
            await WriteSseEventAsync(ctx, "debate-round", round.ToDto(), ct);
        }

        // If already done, close immediately
        if (record.Status is DebateStatus.Completed
                          or DebateStatus.Failed
                          or DebateStatus.Cancelled)
        {
            await WriteSseEventAsync(ctx, "debate-completed", BuildCompletedPayload(record), ct);
            return;
        }

        // Stream live rounds from the channel
        await foreach (var round in record.RoundReader.ReadAllAsync(ct))
        {
            await WriteSseEventAsync(ctx, "debate-round", round.ToDto(), ct);
        }

        // Final event
        await WriteSseEventAsync(ctx, "debate-completed", BuildCompletedPayload(record), ct);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private static async Task WriteSseEventAsync<T>(
        HttpContext ctx,
        string      eventType,
        T           payload,
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
}
