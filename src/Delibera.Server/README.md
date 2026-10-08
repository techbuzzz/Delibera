# Delibera.Server

ASP.NET Core host for [Delibera](https://github.com/techbuzzz/Delibera) — the multi-model AI council framework.

The package exposes the server as a library so you can mount Delibera inside an **existing** ASP.NET Core
application instead of adopting the standalone host. Everything the standalone server does is available as
extension methods, so your own pipeline, authentication, rate limiting and Swagger all stay yours.

## What it gives you

| Area | Endpoint group |
|---|---|
| Debates | create, list, get, cancel, rounds, HTML export, tenant-scoped |
| Streaming | `text/event-stream` with a heartbeat, a `retry:` hint and an explicit terminal event |
| Scenarios | declarative multi-round scenarios with per-member roles, capabilities and weights |
| Corpus | document upload, indexing and RAG-backed knowledge |
| MCP | Model Context Protocol tools over HTTP transport |
| Operations | `/api/v1/health`, OpenTelemetry instrumentation, Serilog wiring |

## Usage

```csharp
using Delibera.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDeliberaServer(builder.Configuration);   // options, providers, DI

var app = builder.Build();
app.UseDeliberaServer();                                     // endpoints + ProblemDetails + OTel

app.MapGet("/", () => Results.Redirect("/api/v1/health"));
app.Run();
```

Every member route lives under `/api/v1`, so it cannot collide with your own endpoints. Health is at
`/api/v1/health` — not `/health`.

## Configuration

Bound from the `Delibera` configuration section: LLM providers and models, RAG backends (Qdrant, PgVector),
debate strategy, defaults, caching, resilience pipelines and telemetry. See the root
[README](https://github.com/techbuzzz/Delibera#readme) for the full section reference and
`docs/Server.md` in the repository for the endpoint contract.

## Dependency

Depends on `Delibera.Core`. You do not need to reference it separately — the package dependency is declared
for you.

## Licence

MIT. See the repository root for details.