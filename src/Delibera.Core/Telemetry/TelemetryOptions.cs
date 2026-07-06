namespace Delibera.Core.Telemetry;

/// <summary>
///    Configuration options for Delibera's OpenTelemetry-style observability.
///    Bound from the <c>Delibera:Telemetry</c> configuration section.
/// </summary>
/// <remarks>
///    <para>
///       All instruments are <c>static readonly</c> and emit zero allocations when no
///       <see cref="System.Diagnostics.ActivityListener" /> or
///       <see cref="System.Diagnostics.Metrics.MeterListener" /> is attached. This means
///       enabling telemetry in <see cref="TelemetryOptions" /> alone has no measurable
///       overhead — it only changes whether <see cref="DeliberaActivitySource" /> instances
///       are started and tags are populated. Actual trace/metric export is the caller's
///       responsibility (via standard <c>OpenTelemetry.*</c> builder APIs).
///    </para>
///    <para>
///       Set <see cref="Enabled" /> to <c>false</c> to short-circuit all instrumentation
///       entirely. When <c>false</c>, <see cref="DeliberaActivitySource" /> returns
///       <c>null</c> activities (the in-box <c>ActivitySource.StartActivity</c> behaviour
///       when no listener is active) and instruments are not recorded.
///    </para>
/// </remarks>
public sealed class TelemetryOptions
{
    /// <summary>Whether telemetry instrumentation is enabled. Default is <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///    The <see cref="System.Diagnostics.ActivitySource.Name" /> used to emit trace spans.
    ///    Match this name in <c>OpenTelemetry.WithTracing(b =&gt; b.AddSource(...))</c>.
    ///    Default is <c>"Delibera.Council"</c>.
    /// </summary>
    public string ActivitySourceName { get; set; } = DeliberaActivitySource.DefaultName;

    /// <summary>
    ///    The <see cref="System.Diagnostics.Metrics.Meter.Name" /> used to emit metrics.
    ///    Match this name in <c>OpenTelemetry.WithMetrics(b =&gt; b.AddMeter(...))</c>.
    ///    Default is <c>"Delibera.Metrics"</c>.
    /// </summary>
    public string MeterName { get; set; } = DeliberaMeter.DefaultName;

    /// <summary>
    ///    Optional service version stamped onto every emitted span and metric instrument
    ///    via <see cref="System.Diagnostics.ActivitySource.Version" /> and
    ///    <see cref="System.Diagnostics.Metrics.Meter.Version" />. Default is <c>"1.0.0"</c>.
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";
}