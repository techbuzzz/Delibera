# delibera-server

ASP.NET Core host for **Delibera** — a framework for collective decision making through structured
AI deliberation. Multiple models reason through a question across rounds, critique each other, and a
**Chairman** weighs the arguments into a final verdict.

This image is the API surface only. For a browser front end, pair it with
[`delibera-webui`](https://hub.docker.com/r/techbuzzz/delibera-webui).

---

## Quick start

```bash
docker run -d --name delibera-server \
  -p 127.0.0.1:5200:8080 \
  -e Delibera__Providers__DefaultEndpoint=http://host.docker.internal:11434 \
  -e Delibera__Models__Fast=llama3.2:3b \
  -e Delibera__Models__Strong=qwen2.5:7b \
  techbuzzz/delibera-server:latest
```

Or bring up the full stack — server, Web UI, Ollama, Qdrant and Redis:

```bash
curl -O https://raw.githubusercontent.com/techbuzzz/Delibera/main/deploy/docker-compose.hub.yml
export DELIBERA_VERSION=10.5.2
docker compose -f docker-compose.hub.yml up -d
```

Pin a release with `DELIBERA_VERSION`; omit it to track `:latest`.

---

## ⚠️ Security

**The API is unauthenticated and unthrottled, and running a debate spends real LLM credits.**
Every published port in the supplied compose file binds to `127.0.0.1` for that reason.

Put an authenticating reverse proxy in front before exposing this stack to any network you do not
control. Do not widen the port bind back to `0.0.0.0` on the assumption that it is "just local".

---

## What it exposes

| Endpoint | Purpose |
| --- | --- |
| `/api/v1/debates` | Create, list, inspect and cancel debates |
| `/api/v1/debates/{id}/stream` | Server-Sent Events, one event per round |
| `/api/v1/scenarios`, `/api/v1/corpus` | Templates and the RAG corpus |
| `/api/v1/health` | Liveness and readiness |

Debates run for 150–200 s and an SSE stream stays open far longer, so read the debate result from
the stream or the record — not from a short client timeout.

---

## Configuration

Every setting is bound from `Delibera__<Section>__<Key>`.

| Variable | Default | Meaning |
| --- | --- | --- |
| `Delibera__Providers__DefaultType` | `Ollama` | Provider family: `Ollama`, `OpenAI`, … |
| `Delibera__Providers__DefaultEndpoint` | `http://host.docker.internal:11434` | Provider base URL |
| `Delibera__Providers__DefaultModel` | `qwen2.5:14b` | Model used when none is specified |
| `Delibera__Providers__ApiKey` | *(empty)* | Required for hosted providers |
| `Delibera__Providers__EmbeddingModel` | `nomic-embed-text` | Embedding model for RAG |
| `Delibera__Models__Fast` / `__Strong` | `llama3.2:3b` / `qwen2.5:7b` | Council sizing tiers |
| `Delibera__MaxRounds` | `4` | Debate rounds |
| `Delibera__Temperature` | `0.7` | Sampling temperature |
| `Delibera__Strategy` | `Standard` | Debate strategy |
| `Delibera__Rag__Enabled` | `false` | Enable RAG |
| `Delibera__Rag__ProviderType` | `Qdrant` | `Qdrant` or `PgVector` |
| `Delibera__Redis__Enabled` | `false` | Enable the Redis orchestrator |
| `Delibera__Redis__CacheEnabled` | `false` | Enable the Redis result cache |
| `Delibera__Output__Directory` | `/app/debate_results` | Where results are written |

### Redis

Off by default — the default path resolves the in-process orchestrator, exactly as before.

Enabling it publishes round events to a Redis Stream so any API instance can stream them over SSE,
and shares debate state across instances. It does **not** provide distributed turn execution: each
debate still runs in the process that accepted it. A bad connection string fails at startup rather
than silently degrading, because a deployment that quietly fell back would look distributed and not be.

---

## Architectures

`linux/amd64` and `linux/arm64`, published together under one manifest per tag.

## Tags

- `10.5.2`, `10.5` — pinned releases
- `latest` — newest release

## Links

- Source: https://github.com/techbuzzz/Delibera
- NuGet: https://www.nuget.org/packages/Delibera.Server
- Release notes: https://github.com/techbuzzz/Delibera/releases

MIT licensed.