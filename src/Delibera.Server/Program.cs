using Delibera.Server.Api.Endpoints;
using Delibera.Server.Api.Filters;
using Delibera.Server.Infrastructure;
using Delibera.Server.Mcp;
using Delibera.Server.Middleware;

var builder = WebApplication.CreateSlimBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck<DeliberaCoreHealthCheck>("delibera-core");

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
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
