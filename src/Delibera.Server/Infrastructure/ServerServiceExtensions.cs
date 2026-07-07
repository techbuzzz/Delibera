using Delibera.Core.DependencyInjection;
using Delibera.Server.Services;
using Delibera.Server.Templates;
using Delibera.Server.Templates.Registry;
using FluentValidation;
using Microsoft.Extensions.Options;
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
