using Delibera.Server.Api.Endpoints;
using Delibera.Server.Api.Filters;
using Delibera.Server.Infrastructure;
using Delibera.Server.Middleware;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateSlimBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .Enrich.WithEnvironmentName()
              .Enrich.WithThreadId());

    // ── Services ──────────────────────────────────────────────────────────────
    builder.Services.AddDeliberaServer(builder.Configuration);

    // ── JSON ──────────────────────────────────────────────────────────────────
    builder.Services.ConfigureHttpJsonOptions(o =>
    {
        o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

    // ── OpenAPI ───────────────────────────────────────────────────────────────
    builder.Services.AddOpenApi();

    // ── Health checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck<DeliberaCoreHealthCheck>("delibera-core");

    var app = builder.Build();

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    // ── Endpoint groups ───────────────────────────────────────────────────────
    var api = app.MapGroup("/api/v1")
                 .AddEndpointFilter<ValidationFilter>();

    api.MapDebateEndpoints();
    api.MapTemplateEndpoints();
    api.MapCorpusEndpoints();

    app.MapHealthChecks("/api/v1/health");

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Delibera.Server terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
