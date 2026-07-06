using System.Diagnostics.Metrics;

namespace Delibera.Core.Telemetry;

/// <summary>
///    Centralised <see cref="Meter" /> for Delibera metrics.
///    All instruments are <c>static readonly</c> — they emit zero allocations when no
///    <see cref="MeterListener" /> is attached (the standard .NET zero-overhead pattern).
/// </summary>
/// <remarks>
///    <para>
///       The meter name defaults to <see cref="DefaultName" /> but can be overridden via
///       <see cref="TelemetryOptions.MeterName" />. When the caller does that, they
///       should also call <c>OpenTelemetry.WithMetrics(b =&gt; b.AddMeter(options.MeterName))</c>.
///    </para>
///    <para>
///       <b>Instruments exposed</b>:
///       <list type="table">
///          <item><term><see cref="DebateDuration"/></term><description>Histogram (ms) of total debate duration.</description></item>
///          <item><term><see cref="RoundDuration"/></term><description>Histogram (ms) of per-round duration, tagged with <c>round_number</c>.</description></item>
///          <item><term><see cref="TokensTotal"/></term><description>Counter of tokens, tagged with <c>member_name</c> and <c>direction</c>.</description></item>
///          <item><term><see cref="CompressionRatio"/></term><description>Gauge of the most recent compression ratio.</description></item>
///          <item><term><see cref="DebatesCompleted"/></term><description>Counter of completed debates, tagged with <c>strategy</c> and <c>success</c>.</description></item>
///       </list>
///    </para>
/// </remarks>
public static class DeliberaMeter
{
    /// <summary>Default meter name used when no override is configured.</summary>
    public const string DefaultName = "Delibera.Metrics";

    private static Meter? _meter;

    /// <summary>
    ///    The current <see cref="Meter" />. Lazily initialised on first access using
    ///    <see cref="DefaultName" /> and version <c>"1.0.0"</c>. Use
    ///    <see cref="Configure(string, string)" /> to override before any debate runs.
    /// </summary>
    public static Meter Instance => _meter ??= new Meter(DefaultName, "1.0.0");

    /// <summary>
    ///    Configures the meter name and version. Call once at startup
    ///    (before any debate execution) when wiring through DI with a non-default
    ///    <see cref="TelemetryOptions.MeterName" />.
    /// </summary>
    public static void Configure(string name, string version = "1.0.0")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _meter?.Dispose();
        _meter = new Meter(name, version);
        ResetInstruments();
    }

    // ──────────────────────────────────────────────
    // Instruments (created lazily via Instance; recreated on Configure)
    // ──────────────────────────────────────────────

    private static Histogram<double>? _debateDuration;
    private static Histogram<double>? _roundDuration;
    private static Counter<long>? _tokensTotal;
    private static Gauge<double>? _compressionRatio;
    private static Counter<long>? _debatesCompleted;

    /// <summary>
    ///    Histogram measuring total debate duration in milliseconds.
    ///    Recorded once per <see cref="Interfaces.ICouncilExecutor.ExecuteAsync" /> call.
    /// </summary>
    public static Histogram<double> DebateDuration =>
        _debateDuration ??= Instance.CreateHistogram<double>(
            "delibera.debate.duration",
            unit: "ms",
            description: "Total debate execution duration in milliseconds.");

    /// <summary>
    ///    Histogram measuring per-round duration in milliseconds.
    ///    Tagged with <c>round_number</c>.
    /// </summary>
    public static Histogram<double> RoundDuration =>
        _roundDuration ??= Instance.CreateHistogram<double>(
            "delibera.round.duration",
            unit: "ms",
            description: "Per-round debate duration in milliseconds.");

    /// <summary>
    ///    Counter accumulating token usage across all LLM calls.
    ///    Tagged with <c>member_name</c> and <c>direction</c> ("input" or "output").
    /// </summary>
    public static Counter<long> TokensTotal =>
        _tokensTotal ??= Instance.CreateCounter<long>(
            "delibera.tokens.total",
            unit: "{tokens}",
            description: "Total tokens consumed by LLM calls, tagged by member and direction.");

    /// <summary>
    ///    Gauge reporting the most recent compression ratio (compressed / original).
    ///    A value of <c>0.5</c> means the compressed text is half the size of the original.
    /// </summary>
    public static Gauge<double> CompressionRatio =>
        _compressionRatio ??= Instance.CreateGauge<double>(
            "delibera.compression.ratio",
            unit: "{ratio}",
            description: "Most recent compression ratio (compressed / original tokens).");

    /// <summary>
    ///    Counter incrementing once per completed debate.
    ///    Tagged with <c>strategy</c> and <c>success</c> ("true"/"false").
    /// </summary>
    public static Counter<long> DebatesCompleted =>
        _debatesCompleted ??= Instance.CreateCounter<long>(
            "delibera.debates.completed",
            unit: "{debates}",
            description: "Total completed debates, tagged by strategy and success.");

    private static void ResetInstruments()
    {
        _debateDuration = null;
        _roundDuration = null;
        _tokensTotal = null;
        _compressionRatio = null;
        _debatesCompleted = null;
    }

    /// <summary>
    ///    Disposes the underlying <see cref="Meter" /> and clears cached instruments.
    ///    Intended for tests and host shutdown.
    /// </summary>
    public static void Shutdown()
    {
        _meter?.Dispose();
        _meter = null;
        ResetInstruments();
    }
}