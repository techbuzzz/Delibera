# Delibera.Server

`Delibera.Server` is a self-hosted ASP.NET Core 10 Minimal API that exposes the Delibera council debate engine over HTTP.
It supports synchronous execution, fire-and-forget background runs, and live **Server-Sent Events** streaming of debate rounds.
All logging is provided by **Microsoft.Extensions.Logging** — no third-party logging framework is required.

## Architecture

The server delegates debate execution to `IDebateOrchestrator` — by default `LocalDebateOrchestrator` (in-process), or `RedisDebateOrchestrator` for distributed deployments. SSE streaming consumes `IDebateOrchestrator.StreamAsync()` so that any API server node can stream rounds regardless of where the debate is executing.

```
┌──────────────────────────────────────────────┐
│  Delibera.Server (ASP.NET Core 10)          │
│  ┌──────────────┐  ┌──────────────────────┐ │
│  │ DebateEndpoints│  │ ScenarioEndpoints   │ │
│  └──────┬───────┘  └──────┬───────────────┘ │
│         │                  │                 │
│  ┌──────▼──────────────────▼──────────────┐  │
│  │ DebateOrchestrationService             │  │
│  │  - RunAsync / Enqueue                  │  │
│  │  - delegates to IDebateOrchestrator    │  │
│  └──────┬────────────────────────────────┘  │
│         │                                    │
│  ┌──────▼────────────────────────────────┐  │
│  │ IDebateOrchestrator                    │  │
│  │  ├─ LocalDebateOrchestrator (default) │  │
│  │  └─ RedisDebateOrchestrator          │  │
│  └───────────────────────────────────────┘  │
│                                              │
│  ┌───────────────────────────────────────┐  │
│  │ SseDebateStreamWriter                │  │
│  │  - consumes StreamAsync() events     │  │
│  │  - emits SSE: debate-round,          │  │
│  │    debate-completed, debate-failed,  │  │
│  │    debate-cancelled                  │  │
│  └───────────────────────────────────────┘  │
└──────────────────────────────────────────────┘
```

---

## Quick Start

### Run with Docker Compose

```bash
# from repo root
docker compose -f docker-compose.server.yml up
```

Example `docker-compose.server.yml`:

```yaml
services:
  delibera-server:
    build:
      context: .
      dockerfile: src/Delibera.Server/Dockerfile
    ports: ["8080:8080"]
    environment:
      Delibera__Providers__DefaultEndpoint: http://ollama:11434
      Delibera__Rag__Enabled: "false"
    depends_on: [ollama]

  ollama:
    image: ollama/ollama
    volumes: ["ollama_data:/root/.ollama"]
    ports: ["11434:11434"]

volumes:
  ollama_data:
```

### Run a debate (curl)

```bash
curl -s -X POST http://localhost:8080/api/v1/debates \
  -H "Content-Type: application/json" \
  -d '{
    "templateId": "code-review",
    "question": "Review the following PR diff for security and quality issues.",
    "inputData": { "diff": "--- a/auth.cs\n+++ b/auth.cs\n@@ ... @@" }
  }' | jq .
```

### Stream a debate live (SSE)

```bash
# 1. Enqueue async
DEBATE_ID=$(curl -s -X POST http://localhost:8080/api/v1/debates/async \
  -H "Content-Type: application/json" \
  -d '{"templateId":"risk-committee","question":"Approve cloud migration to AWS?"}' \
  | jq -r '.debateId')

# 2. Stream rounds
curl -N http://localhost:8080/api/v1/debates/$DEBATE_ID/stream
```

---

## API Reference

### Debates

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/v1/debates` | Run debate synchronously; returns 201 with full verdict |
| `POST` | `/api/v1/debates/async` | Enqueue debate in background; returns 202 immediately |
| `GET` | `/api/v1/debates/{id}` | Get debate status + verdict |
| `GET` | `/api/v1/debates/{id}/result` | Get structured verdict only (409 if not completed) |
| `GET` | `/api/v1/debates/{id}/stream` | SSE stream — `debate-round` and `debate-completed` events |
| `GET` | `/api/v1/debates/{id}/rounds` | Paginated list of completed rounds (`?page=1&pageSize=20`) |
| `GET` | `/api/v1/debates` | List debates (`?templateId=`, `?status=`, `?page=`, `?pageSize=`) |
| `DELETE` | `/api/v1/debates/{id}` | Cancel a running/pending debate |
| `GET` | `/api/v1/debates/{id}/export/markdown` | Download full transcript as `.md` |

### Templates

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/v1/templates` | List all registered templates |
| `GET` | `/api/v1/templates/{id}` | Get template details (roles, strategy, etc.) |

### Corpora (RAG)

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/v1/corpora` | List corpora |
| `POST` | `/api/v1/corpora` | Create a new corpus |
| `POST` | `/api/v1/corpora/{id}/documents` | Upload & index a document |
| `GET` | `/api/v1/corpora/{id}/documents` | List documents in corpus |
| `DELETE` | `/api/v1/corpora/{id}/documents/{docId}` | Remove a document |
| `POST` | `/api/v1/corpora/{id}/search` | Semantic search against corpus |

### Health

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/v1/health` | Liveness + template-registry check |

---

## SSE Event Protocol

All events are JSON-encoded.

```
event: debate-round
data: { "roundNumber": 1, "strategy": "CritiqueDebate", "messages": [...], "chairmanSummary": "..." }

event: debate-completed
data: { "debateId": "abc123", "status": "Completed", "verdict": "Approve with conditions.", "completedAt": "..." }

event: debate-error
data: { "debateId": "abc123", "status": "Failed", "error": "LLM provider timeout" }

event: debate-cancelled
data: { "debateId": "abc123" }
```

The stream first replays all rounds completed before the client connected, then streams live rounds from `IDebateOrchestrator.StreamAsync()`, and closes with a terminal event (`debate-completed`, `debate-error`, or `debate-cancelled`).

### Response Fields

`DebateResponse` includes these additional fields as of v10.3.0:

| Field | Type | Description |
|-------|------|-------------|
| `label` | `string?` | Human-readable label for the debate (from `ScenarioRequest.Label` or `CreateDebateRequest.Question`) |
| `cacheHit` | `bool?` | `true` if the result was served from cache |
| `cacheKey` | `string?` | The SHA-256 cache key when caching is enabled |

---

## Built-in Templates

| Template ID | Display Name | Vertical | Strategy | Rounds |
|-------------|-------------|----------|----------|--------|
| `code-review` | Code Review Council | Engineering | CritiqueDebate | 3 |
| `risk-committee` | Risk Committee Council | Enterprise Governance | ConsensusDebate | 5 |
| `requirements-review` | Requirements Council | Product / Requirements | ConsensusDebate | 4 |
| `legal-contract-review` | Legal Risk Council | Legal / Compliance | CritiqueDebate | 4 |
| `architecture-decision` | Architecture Decision Council | Engineering | ConsensusDebate | 4 |

Custom templates implement `IServerTemplate` and register via DI:

```csharp
services.AddSingleton<IServerTemplate, MyCustomTemplate>();
```

---

## Logging & Observability

Logging uses **Microsoft.Extensions.Logging** exclusively — no Serilog or other third-party sinks.
Configure log levels in `appsettings.json`:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Delibera": "Debug"
  }
}
```

OpenTelemetry traces and metrics are exported via OTLP (configure `Delibera:Server:Telemetry:OtlpEndpoint`)
or fall back to the console exporter in development.

---

## Configuration Reference

| Key | Default | Description |
|-----|---------|-------------|
| `Delibera:Strategy` | `Standard` | Default debate strategy |
| `Delibera:MaxRounds` | `4` | Default maximum rounds |
| `Delibera:Temperature` | `0.7` | LLM sampling temperature |
| `Delibera:Providers:DefaultType` | `Ollama` | LLM backend (`Ollama` \| `OpenAI` \| `AzureOpenAI`) |
| `Delibera:Providers:DefaultEndpoint` | `http://ollama:11434` | LLM base URL |
| `Delibera:Providers:DefaultModel` | `qwen2.5:14b` | Model name |
| `Delibera:Providers:EmbeddingModel` | `nomic-embed-text` | Embedding model |
| `Delibera:Rag:Enabled` | `false` | Enable RAG / Knowledge Keeper |
| `Delibera:Rag:ProviderType` | `Qdrant` | Vector store (`Qdrant` \| `PgVector`) |
| `Delibera:Rag:ConnectionString` | — | Connection string for vector store |
| `Delibera:Output:Directory` | `./debate_results` | Where to persist debate result files |
| `Delibera:Server:Telemetry:OtlpEndpoint` | _(empty)_ | OTLP collector endpoint |

All keys can be overridden via environment variables using the `__` separator:

```bash
Delibera__Providers__DefaultEndpoint=http://my-ollama:11434
```

---

## Multi-Tenant Headers

| Header | Description |
|--------|-------------|
| `X-Tenant-Id` | Tenant identifier; isolates debate records per tenant |
| `X-Correlation-Id` | Optional correlation ID propagated to all logs and OTel spans |

---

## Development

```bash
cd src
dotnet run --project Delibera.Server
# OpenAPI UI available at https://localhost:5001/openapi
```

---

## Distributed Debates (Redis)

By default, `Delibera.Server` uses `LocalDebateOrchestrator` — all debates run in-process. To enable multi-instance deployments with Redis Streams for event broadcasting:

```csharp
// Program.cs
builder.Services.AddDelibera(builder.Configuration);
builder.Services.AddRedisDebateOrchestrator(builder.Configuration);
```

```json
{
  "Delibera:Redis": {
    "ConnectionString": "localhost:6379,abortConnect=false",
    "EventStreamKey": "delibera:events",
    "StateKeyPrefix": "delibera:state:"
  }
}
```

See [docs/distributed-debates.md](distributed-debates.md) for the full architecture diagram and configuration reference.

---

## Result Caching

Enable debate result caching to avoid re-running identical debates:

```csharp
// In-Memory (development)
builder.Services.AddDelibera(builder.Configuration)
    .UseInMemoryCache(ttl: TimeSpan.FromHours(1));

// File-based
builder.Services.AddDelibera(builder.Configuration)
    .UseFileCache(directory: "./debate-cache", ttl: TimeSpan.FromDays(7));

// Redis (production)
builder.Services.AddRedisDebateOrchestrator(builder.Configuration);
builder.Services.UseRedisCache(ttl: TimeSpan.FromHours(24));
```

See [docs/caching.md](caching.md) for `CacheBehavior` modes and cache key generation.
