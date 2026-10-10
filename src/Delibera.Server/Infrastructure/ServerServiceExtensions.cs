using Delibera.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

namespace Delibera.Server.Infrastructure;

public static class ServerServiceExtensions
{
   /// <summary>
   ///    Name of the CORS policy registered when <c>Delibera:Server:Cors:AllowedOrigins</c> is
   ///    non-empty. <c>UseCors</c> must be given this exact name, and may only be called at all
   ///    when the policy exists — ASP.NET throws at request time for an unknown policy, which is
   ///    a far worse failure mode than simply not enabling CORS.
   /// </summary>
   public const string CorsPolicyName = "DeliberaCors";

   /// <summary>
   ///    Registers a named CORS policy, but only when origins are explicitly configured.
   /// </summary>
   /// <remarks>
   ///    <para>
   ///       The default registration path is empty on purpose: with no origins nothing is added
   ///       to the container, so a deployment that never heard of this option behaves exactly as
   ///       it did before. <c>AddCors</c> on its own emits no headers, but leaving the policy
   ///       absent also means <c>UseCors</c> can be guarded by
   ///       <see cref="IsCorsEnabled"/> instead of throwing on first request.
   ///    </para>
   ///    <para>
   ///       Origins are matched exactly by the CORS middleware. There is no
   ///       <c>SetIsOriginAllowed(_ =&gt; true)</c> path here on purpose: this API is
   ///       unauthenticated, so "any origin" would let any page on the internet spend credits
   ///       through someone else's browser.
   ///    </para>
   /// </remarks>
   public static IServiceCollection AddDeliberaCors(
      this IServiceCollection services,
      IConfiguration configuration)
   {
      var cors = configuration
         .GetSection(DeliberaServerOptions.SectionName)
         .Get<DeliberaServerOptions>()?.Cors ?? new CorsGateOptions();

      var origins = cors.AllowedOrigins
         .Where(o => !string.IsNullOrWhiteSpace(o))
         .Select(o => o.Trim())
         .Distinct(StringComparer.OrdinalIgnoreCase)
         .ToArray();

      if (origins.Length == 0)
         return services;

      // `*` with credentials is rejected by the framework at policy-build time with a message
      // that does not name the config key. Fail here instead, where the operator can see it.
      if (cors.AllowCredentials && origins.Contains("*", StringComparer.OrdinalIgnoreCase))
         throw new InvalidOperationException(
            $"{DeliberaServerOptions.SectionName}:Cors:AllowCredentials is true while " +
            $"AllowedOrigins contains '*'. Name the origins explicitly — a wildcard is never " +
            "valid alongside credentials.");

      services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
      {
         policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            // The correlation id is how a caller finds its entry in the server log; without this
            // the browser hides it from a cross-origin client.
            .WithExposedHeaders("X-Correlation-Id");

         if (cors.AllowCredentials)
            policy.AllowCredentials();
      }));

      return services;
   }

   public static IServiceCollection AddDeliberaServer(
      this IServiceCollection services,
      IConfiguration configuration)
   {
      // Options
      services.Configure<DeliberaServerOptions>(
         configuration.GetSection(DeliberaServerOptions.SectionName));

      // Delibera.Core
      services.AddDelibera(configuration);

      // Redis — opt-in, off by default, and validated eagerly so a typo in the
      // connection string names itself at startup rather than at the first debate.
      services.AddDeliberaRedis(configuration);

      // CORS — opt-in, off by default. The Web UI's same-origin BFF route is the supported path;
      // this exists only for a deliberately static client such as the GitHub Pages build.
      services.AddDeliberaCors(configuration);

      // Business services
      services.AddSingleton<ITemplateRegistry, TemplateRegistry>();
      services.AddSingleton<IDebateOrchestrationService, DebateOrchestrationService>();
      // Shared by corpus indexing and the Knowledge Keeper so both sides agree on the embedding
      // model and vector size. Registered unconditionally: it reports Enabled == false rather than
      // throwing when Rag is switched off.
      services.AddSingleton<ServerRagProviderFactory>();
      services.AddSingleton<ICorpusService, CorpusService>();

      // Validators (auto-scan assembly)
      services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

      // ── MCP Server (HTTP transport) ───────────────────────────────────────
      // Exposes Delibera council as MCP tools at /mcp.
      // Claude Desktop, Cursor, or any MCP client can connect to:
      //   http://localhost:5200/mcp
      services
         .AddMcpServer()
         .WithHttpTransport()
         .WithToolsFromAssembly(); // auto-discovers [McpServerToolType] in this assembly

      // OpenTelemetry
      var otelOptions = configuration
         .GetSection(DeliberaServerOptions.SectionName)
         .Get<DeliberaServerOptions>()?.Telemetry;

      services
         .AddOpenTelemetry()
         .ConfigureResource(r => r.AddService("delibera-server"))
         .WithTracing(t =>
         {
            t.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddSource("Delibera.*");

            if (!string.IsNullOrEmpty(otelOptions?.OtlpEndpoint))
               t.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.OtlpEndpoint));
            else
               t.AddConsoleExporter();
         })
         .WithMetrics(m =>
         {
            m.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddRuntimeInstrumentation()
               .AddMeter("Delibera.*");

            if (!string.IsNullOrEmpty(otelOptions?.OtlpEndpoint))
               m.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.OtlpEndpoint));
            else
               // Mirror the tracing fallback. Without it every custom Delibera metric —
               // round duration, token usage, cache hits, compression ratio — was recorded
               // and then dropped, with no error, because an empty OTLP endpoint is a
               // perfectly valid configuration.
               m.AddConsoleExporter();
         });

      return services;
   }

   /// <summary>
   ///    Wires the Redis-backed debate orchestrator when <c>Delibera:Redis:Enabled</c> is true.
   /// </summary>
   /// <remarks>
   ///    <para>
   ///       What Redis actually buys today, stated plainly because the alternative reading is
   ///       a documentation bug waiting to happen: round events are published to a Redis Stream
   ///       so any API instance can stream them over SSE, debate state is shared, and results
   ///       can be cached. It does <b>not</b> distribute turn execution —
   ///       <c>DebateWorkerService.ProcessMessageAsync</c> is an explicit no-op placeholder and
   ///       the orchestrator still runs each debate in-process. So this is not horizontal
   ///       debate scaling, and nothing below should be described as if it were.
   ///    </para>
   ///    <para>
   ///       Two deliberate choices:
   ///    </para>
   ///    <list type="bullet">
   ///       <item>
   ///          The connection is validated eagerly (parse + endpoint check) so a bad
   ///          connection string fails at startup with a message naming the config key. A
   ///          silent fallback to the local orchestrator would leave a deployment that
   ///          looks distributed and is not. Transient unavailability is handled separately by
   ///          <c>abortConnect=false</c>, which is why compose can start Redis and the server
   ///          in either order.
   ///       </item>
   ///       <item>
   ///          <c>DebateWorkerService</c> is NOT registered as a hosted service. It is a
   ///          placeholder whose only effect would be an <c>XREADGROUP</c> poll loop against a
   ///          stream nobody publishes to — real Redis load for no behaviour.
   ///       </item>
   ///    </list>
   /// </remarks>
   public static IServiceCollection AddDeliberaRedis(
      this IServiceCollection services,
      IConfiguration configuration)
   {
      var section = configuration.GetSection(RedisGateOptions.SectionName);
      var gate = section.Get<RedisGateOptions>() ?? new RedisGateOptions();

      if (!gate.Enabled)
      {
         // Nothing is registered: IDebateOrchestrator stays whatever Delibera.Core bound
         // (LocalDebateOrchestrator), so the default path is byte-identical to before Redis
         // was an option at all.
         return services;
      }

      var connectionString = section["ConnectionString"];
      ValidateRedisConnectionString(connectionString, section);

      // Assign through a non-nullable local: ValidateRedisConnectionString throws on null or
      // blank, but that guarantee does not flow to the compiler, and CI builds with
      // -warnaserror (publish-nuget.yml).
      var validated = connectionString!;

      // ConfigurationOptions.Parse is the same parse ConnectionMultiplexer performs, so a
      // string rejected here would have thrown later inside the orchestrator's constructor
      // with no indication of which config key was at fault.
      var options = ConfigurationOptions.Parse(validated);
      options.AbortOnConnectFail = false; // survive a cold/slow Redis in compose ordering

      services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));

      // Binds RedisOrchestratorOptions to the same section and replaces IDebateOrchestrator.
      services.AddRedisDebateOrchestrator(configuration);

      if (gate.CacheEnabled)
         services.UseRedisCache(gate.CacheTtl);

      return services;
   }

   /// <summary>
   ///    Fails fast when Redis is switched on with an unusable connection string.
   /// </summary>
   private static void ValidateRedisConnectionString(
      string? connectionString,
      IConfigurationSection section)
   {
      const string key = $"{RedisGateOptions.SectionName}:ConnectionString";

      if (string.IsNullOrWhiteSpace(connectionString))
         throw new InvalidOperationException(
            $"{RedisGateOptions.SectionName}:Enabled is true but {key} is empty. " +
            $"Set {key} (for example 'redis:6379,abortConnect=false') or set " +
            $"{RedisGateOptions.SectionName}:Enabled to false.");

      ConfigurationOptions parsed;
      try
      {
         parsed = ConfigurationOptions.Parse(connectionString);
      }
      catch (Exception ex)
      {
         throw new InvalidOperationException(
            $"{key} ('{connectionString}') could not be parsed: {ex.Message}", ex);
      }

      if (parsed.EndPoints.Count == 0)
         throw new InvalidOperationException(
            $"{key} ('{connectionString}') declares no Redis endpoint.");
   }
}
