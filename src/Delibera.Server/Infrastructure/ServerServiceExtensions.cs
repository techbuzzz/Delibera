using Delibera.Core.DependencyInjection;
using Delibera.Server.Services;
using Delibera.Server.Templates;
using Delibera.Server.Templates.Registry;
using FluentValidation;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Delibera.Server.Infrastructure;

public static class ServerServiceExtensions
{
    public static IServiceCollection AddDeliberaServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<DeliberaServerOptions>(
            configuration.GetSection(DeliberaServerOptions.SectionName));

        // Delibera.Core
        services.AddDelibera(configuration, "Delibera");

        // Business services
        services.AddSingleton<ITemplateRegistry, TemplateRegistry>();
        services.AddSingleton<IDebateOrchestrationService, DebateOrchestrationService>();
        services.AddSingleton<ICorpusService, CorpusService>();

        // Validators (auto-scan assembly)
        services.AddValidatorsFromAssemblyContaining<Program>(lifetime: ServiceLifetime.Singleton);

        // ── MCP Server (HTTP transport) ───────────────────────────────────────
        // Exposes Delibera council as MCP tools at /mcp.
        // Claude Desktop, Cursor, or any MCP client can connect to:
        //   http://localhost:5200/mcp
        services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly();  // auto-discovers [McpServerToolType] in this assembly

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
            });

        return services;
    }
}
