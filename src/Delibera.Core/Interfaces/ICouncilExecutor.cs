using Delibera.Core.Council;
using Delibera.Core.Telemetry;

namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for executing a configured council debate session.
///    Enables dependency injection and testability of debate execution.
/// </summary>
public interface ICouncilExecutor
{
   /// <summary>Council participants.</summary>
   IReadOnlyList<CouncilMember> Members { get; }

   /// <summary>Chairman (may be <c>null</c>).</summary>
   CouncilMember? Chairman { get; }

   /// <summary>Knowledge Keeper (may be <c>null</c>).</summary>
   KnowledgeKeeper? KnowledgeKeeper { get; }

   /// <summary>Operator (may be <c>null</c>).</summary>
   Operator? Operator { get; }

   /// <summary>Debate strategy.</summary>
   IDebateStrategy Strategy { get; }

    /// <summary>Context compressor (may be <c>null</c> if compression is disabled).</summary>
    IContextCompressor? Compressor { get; }

    /// <summary>
    ///    Whether OpenTelemetry-style instrumentation is active for this executor. When
    ///    <c>true</c>, <see cref="ExecuteAsync"/> emits <see cref="System.Diagnostics.Activity"/>
    ///    spans via <see cref="DeliberaActivitySource"/> and records metrics via
    ///    <see cref="DeliberaMeter"/>. Default is <c>false</c> unless
    ///    <see cref="ICouncilBuilder.WithTelemetry(TelemetryOptions?)"/> was called.
    /// </summary>
    bool IsTelemetryEnabled { get; }

    /// <summary>
    ///    The configured debate-level wall-clock timeout. <c>null</c> means no timeout.
    ///    Set via <see cref="ICouncilBuilder.WithTimeout(TimeSpan)"/>.
    /// </summary>
    TimeSpan? DebateTimeout { get; }

    /// <summary>
    ///    The result of the most recent <see cref="StreamDebateAsync"/> call, once the
    ///    stream has completed. <c>null</c> while the stream is in progress, before any
    ///    streaming call, or after <see cref="ExecuteAsync"/> was used instead of
    ///    streaming. Useful when a consumer wants both the live round stream and the
    ///    aggregated <see cref="DebateResult"/> (with execution logs, token stats, etc.)
    ///    at the end.
    /// </summary>
    DebateResult? LastStreamedResult { get; }

   /// <summary>
   ///    Optional <see cref="ILogger" /> used by the executor to surface progress
   ///    (Chairman actions, rounds, compression, errors, …) to a host's logging pipeline.
   ///    When <c>null</c>, only the <see cref="OnLog" /> event and the
   ///    <see cref="ExecutionLogs" /> collection are populated.
   /// </summary>
   ILogger? Logger { get; }

   /// <summary>
   ///    Execution logs collected during the debate.
   ///    Empty until <see cref="ExecuteAsync" /> is called.
   /// </summary>
   IReadOnlyList<ExecutionLog> ExecutionLogs { get; }

   /// <summary>Invoked after each round completes.</summary>
   event Action<DebateRound>? OnRoundCompleted;

   /// <summary>
   ///    Invoked for every <see cref="ExecutionLog" /> entry produced during
   ///    <see cref="ExecuteAsync" />. Useful for streaming progress to a console
   ///    or another observer without having to wait until the debate finishes.
   /// </summary>
   event Action<ExecutionLog>? OnLog;

   /// <summary>
   ///    Invoked when a non-fatal error is caught internally (e.g. failed MCP
   ///    tool call, failed knowledge index). The debate continues. Fatal errors
   ///    are surfaced via the <see cref="ExecuteAsync" /> exception path.
   /// </summary>
   event Action<Exception, string>? OnError;

    /// <summary>
    ///    Runs the debate and returns the full result.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Complete debate result with rounds, verdict, logs, and metadata.</returns>
    Task<DebateResult> ExecuteAsync(CancellationToken ct = default);

    /// <summary>
    ///    Streams the debate round-by-round via <see cref="IAsyncEnumerable{DebateRound}"/>
    ///    so consumers (ASP.NET Core SSE, WebSocket, Blazor, CLI) can react to each round
    ///    as it completes — without waiting for the full debate to finish.
    /// </summary>
    /// <param name="ct">Cancellation token. Cancelling mid-stream aborts the current LLM
    /// call and stops iteration cleanly via <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    ///    An async enumerable yielding one <see cref="DebateRound"/> per round, including
    ///    the final Chairman-verdict round. Each yielded round has its
    ///    <see cref="DebateRound.Total"/> and <see cref="DebateRound.IsFinal"/> properties
    ///    populated so consumers can render <c>"Round 2 / 4"</c> progress UIs.
    /// </returns>
    /// <remarks>
    ///    <para>
    ///       The default implementation (DIM) calls <see cref="ExecuteAsync"/> and yields
    ///       all rounds at the end — preserving backward compatibility for any external
    ///       <see cref="ICouncilExecutor"/> implementation. The built-in
    ///       <see cref="Council.CouncilExecutor"/> overrides this to stream rounds
    ///       live as they complete.
    ///    </para>
    ///    <para>
    ///       <b>Usage — CLI live output:</b>
    ///       <code>
    /// await foreach (var round in executor.StreamDebateAsync(ct))
    ///     Console.WriteLine($"[Round {round.RoundNumber}/{round.Total}] {round.RoundName}");
    ///       </code>
    ///    </para>
    ///    <para>
    ///       <b>Usage — ASP.NET Core SSE:</b>
    ///       <code>
    /// app.MapGet("/debate/stream", async (HttpContext ctx, ICouncilExecutor executor) =>
    /// {
    ///     ctx.Response.Headers.ContentType = "text/event-stream";
    ///     await foreach (var round in executor.StreamDebateAsync(ctx.RequestAborted))
    ///     {
    ///         var json = JsonSerializer.Serialize(round);
    ///         await ctx.Response.WriteAsync($"data: {json}\n\n");
    ///         await ctx.Response.Body.FlushAsync();
    ///     }
    /// });
    ///       </code>
    ///    </para>
    /// </remarks>
    async IAsyncEnumerable<DebateRound> StreamDebateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await ExecuteAsync(ct).ConfigureAwait(false);
        foreach (var round in result.Rounds)
            yield return round;
    }

   /// <summary>
   ///    Compresses text using the configured compressor, with optional caching.
   ///    Returns the original text unchanged if no compressor is configured.
   /// </summary>
   /// <param name="text">Text to compress.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Compressed context (or pass-through if no compressor).</returns>
   Task<CompressedContext> CompressTextAsync(string text, CancellationToken ct = default);

   /// <summary>
   ///    Returns a formatted summary of the council configuration.
   /// </summary>
   string GetInfo();
}