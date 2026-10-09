namespace Delibera.Server.Infrastructure;

public sealed class DeliberaServerOptions
{
   /// <summary>
   ///    Configuration section. Must match the layout in appsettings.json
   ///    (<c>"Delibera": { "Server": { … } }</c>) — a mismatch here silently leaves
   ///    every option at its default, including the OTLP endpoint.
   /// </summary>
   public const string SectionName = "Delibera:Server";

   /// <summary>Default tenant id used when X-Tenant-Id header is absent.</summary>
   public string DefaultTenantId { get; init; } = "default";

   /// <summary>Maximum number of concurrent debates per server instance.</summary>
   public int MaxConcurrentDebates { get; init; } = 20;

   /// <summary>How long to keep completed debate results in memory store (minutes).</summary>
   public int DebateRetentionMinutes { get; init; } = 60;

   public TelemetryOptions Telemetry { get; init; } = new();
   public AuthOptions Auth { get; init; } = new();
   public CorsGateOptions Cors { get; init; } = new();
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

/// <summary>
///    Cross-origin access for browser clients. Empty by default.
/// </summary>
/// <remarks>
///    <para>
///       Empty means <b>no CORS policy is registered at all</b>, which is the shipped behaviour:
///       the Web UI reaches the API through its own same-origin BFF route and never needs it. The
///       header being absent is also what stops a random web page from <i>reading</i> a response —
///       without CORS a cross-origin <c>fetch</c> still fires the request, it just cannot see the
///       answer, so leaving this on by default would quietly add a browser-side CSRF vector to an
///       unauthenticated, unthrottled API that spends real credits.
///    </para>
///    <para>
///       Set it only for a deliberately static client — the GitHub Pages build of the Web UI —
///       and list origins explicitly. There is no wildcard switch on purpose.
///    </para>
/// </remarks>
/// <remarks>
///    Named <c>CorsGateOptions</c> rather than <c>CorsOptions</c> so it cannot shadow
///    <c>Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions</c> in any file importing both
///    namespaces.
/// </remarks>
public sealed class CorsGateOptions
{
   /// <summary>
   ///    Exact browser origins permitted to call the API cross-origin, e.g.
   ///    <c>https://techbuzzz.github.io</c>. Empty — the default — registers no policy.
   /// </summary>
   public string[] AllowedOrigins { get; init; } = [];

   /// <summary>
   ///    Whether to allow credentialed requests. Leave false unless the API sits behind an
   ///    authenticating proxy that sets cookies; with cookies anywhere in play a wildcard
   ///    origin would be rejected outright.
   /// </summary>
   public bool AllowCredentials { get; init; } = false;
}
