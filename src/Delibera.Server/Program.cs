using Delibera.Server.Api.Endpoints;
using Delibera.Server.Api.Filters;
using Delibera.Server.Infrastructure;
using Delibera.Server.Mcp;
using Delibera.Server.Middleware;
using Serilog;

var builder = WebApplication.CreateSlimBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
// Serilog was a dependency and CorrelationIdMiddleware pushed a Serilog LogContext
// property, but nothing ever called UseSerilog — so the correlation id reached no log
// line at all while the response advertised it. Serilog now owns the pipeline; a console
// sink is configured explicitly so the app logs even when appsettings has no "Serilog"
// section, and anything under that section is layered on top.
builder.Host.UseSerilog((context, services, configuration) => configuration
   .ReadFrom.Configuration(context.Configuration)
   .ReadFrom.Services(services)
   .Enrich.FromLogContext()
   .Enrich.WithProperty("Application", "Delibera.Server")
   .WriteTo.Console());

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddDeliberaServer(builder.Configuration);

// ── JSON ──────────────────────────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(o =>
{
   o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
   o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
   o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Error handling ─────────────────────────────────────────────────────────────
// Without this, an unhandled exception produced a bare 500 with no body — and the slim
// builder does not add the developer exception page either, so the client got nothing
// parseable. ProblemDetailsExceptionHandler writes an RFC 7807 payload in every
// environment and keeps the exception message out of non-Development responses.
builder.Services.AddProblemDetails(options =>
   options.CustomizeProblemDetails = context =>
   {
      // A status-code ProblemDetails (404, 405) carries only a W3C traceId, which is not
      // the X-Correlation-Id the client sent and cannot be used to find the log entry.
      // The exception path already adds it; this makes the two agree.
      if (context.HttpContext.Items.TryGetValue(
             ProblemDetailsExceptionHandler.CorrelationIdHeader, out var value)
          && value is string correlationId)
      {
         context.ProblemDetails.Extensions["correlationId"] = correlationId;
      }
   });
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
   .AddCheck<DeliberaCoreHealthCheck>("delibera-core");

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// UseExceptionHandler must come first so it also covers the middleware below it.
app.UseExceptionHandler();
// Gives 4xx/5xx responses that have no body (unmatched routes, 405s) a ProblemDetails body.
app.UseStatusCodePages();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

if (app.Environment.IsDevelopment())
   app.MapOpenApi();

// ── REST endpoint groups ──────────────────────────────────────────────────────
var api = app.MapGroup("/api/v1")
   .AddEndpointFilter<ValidationFilter>();

api.MapDebateEndpoints();
api.MapTemplateEndpoints();
api.MapCorpusEndpoints();
api.MapScenarioEndpoints();

app.MapHealthChecks("/api/v1/health");

// ── MCP endpoint (Model Context Protocol) ────────────────────────────────────
// Claude Desktop / Cursor / any MCP client → http://localhost:5200/mcp
app.MapDeliberaMcp("/mcp");

await app.RunAsync();

// Exposed so the test project can spin the real pipeline up with WebApplicationFactory.
// Top-level statements generate an internal Program, which the factory cannot see.
public partial class Program;
