namespace Delibera.Server.Infrastructure;

public sealed class DeliberaServerOptions
{
    public const string SectionName = "DeliberaServer";

    /// <summary>Default tenant id used when X-Tenant-Id header is absent.</summary>
    public string DefaultTenantId { get; init; } = "default";

    /// <summary>Maximum number of concurrent debates per server instance.</summary>
    public int MaxConcurrentDebates { get; init; } = 20;

    /// <summary>How long to keep completed debate results in memory store (minutes).</summary>
    public int DebateRetentionMinutes { get; init; } = 60;

    public TelemetryOptions Telemetry { get; init; } = new();
    public AuthOptions Auth { get; init; } = new();
}

public sealed class TelemetryOptions
{
    public string? OtlpEndpoint { get; init; }
    public bool EnableConsoleExporter { get; init; } = true;
}

public sealed class AuthOptions
{
    public bool Enabled { get; init; } = false;
    public string? ApiKey { get; init; }
}
