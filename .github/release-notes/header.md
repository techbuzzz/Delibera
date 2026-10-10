# Delibera {{VERSION}}

> **⚠️ Before you expose this anywhere:** the API is unauthenticated and unthrottled, and a debate
> spends real LLM credits. Published ports bind to `127.0.0.1`. Put an authenticating reverse proxy
> in front before putting this stack on a network you do not control.

## Get it

| | |
| --- | --- |
| NuGet | [`Delibera.Core`](https://www.nuget.org/packages/Delibera.Core) · [`Delibera.Server`](https://www.nuget.org/packages/Delibera.Server) · [`Delibera.Redis`](https://www.nuget.org/packages/Delibera.Redis) |
| Docker | [`techbuzzz/delibera-server`](https://hub.docker.com/r/techbuzzz/delibera-server) · [`techbuzzz/delibera-webui`](https://hub.docker.com/r/techbuzzz/delibera-webui) — `linux/amd64` + `linux/arm64` |
| Docs | [techbuzzz.github.io/Delibera](https://techbuzzz.github.io/Delibera/) |

```bash
export DELIBERA_VERSION={{VERSION}}
docker compose -f docker-compose.hub.yml up -d
```

```bash
dotnet add package Delibera.Core   # {{VERSION}}
```

---