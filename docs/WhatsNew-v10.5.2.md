# Delibera v10.5.2 — What's New

> **Release type: feature release that contains two breaking behaviour changes.** Published ports
> now bind to `127.0.0.1`, and `Delibera.Server` gains a transitive dependency on `Delibera.Redis`.
> Both are deliberate and both are documented below. Nothing else in the public API changed — no
> member was removed from an existing interface, and no signature changed.
>
> The patch number is kept because the tag and merge history already declare this release as
> v10.5.2. If you are versioning fresh and the ports matter to you, treat the deployment section as
> the migration guide.

This release ships a browser front end for the debate workflow, makes Redis reachable from inside
the server instead of being an unreachable package, publishes Docker images to Docker Hub, and fixes
four Web UI defects — one of which made creating a debate through the UI impossible.

---

## Table of contents

- [Breaking changes](#breaking-changes)
- [The Web UI](#the-web-ui)
- [The proxy bug that made every POST fail](#the-proxy-bug-that-made-every-post-fail)
- [Redis, actually wired up](#redis-actually-wired-up)
- [Docker Hub images](#docker-hub-images)
- [Verification](#verification)
- [Upgrading](#upgrading)

---

## Breaking changes

### Published ports bind to `127.0.0.1`

Every published port in `docker-compose.yml` was bound to `0.0.0.0`. All of them now bind to
`127.0.0.1`.

This is not hardening theatre — it is the actual enforcement. **The API is unauthenticated and
unthrottled, and running a debate spends real LLM credits.** That was already true, but publishing
images makes it trivially reachable from anywhere the host can route to.

```yaml
# before — reachable from the LAN
ports:
  - "5200:5200"

# after — localhost only
ports:
  - "127.0.0.1:5200:5200"
```

**If you need LAN or public access**, put an authenticating reverse proxy in front and bind its port
explicitly. Do not simply widen the bind back to `0.0.0.0` and call it done.

A host currently reaching `Delibera.Server` from another machine on its LAN will stop being able to
after this upgrade. That is the intended effect.

### `Delibera.Server` now depends on `Delibera.Redis`

The `ProjectReference` flows into the nuspec, so consumers of `Delibera.Server` now also pull
`Delibera.Redis` and `StackExchange.Redis`.

This was accepted deliberately rather than hidden with `PrivateAssets="all"`, which would remove
`AddRedisDebateOrchestrator` from the published package — the one thing the reference exists to
expose. Both packages ship in the same GA matrix at the same version.

---

## The Web UI

`Delibera.WebUI` is a Nuxt 4 application, shipped as a **second container** alongside
`delibera-server`. `docker compose up` starts it with the rest.

Scope: list debates, create one from a registered template, watch rounds arrive live, read the
verdict with token stats, export Markdown, cancel a running debate.

No UI kit and no Tailwind. This becomes a published image, and every dependency is image weight and
supply-chain surface for what is a handful of forms and a timeline.

### Why it talks to a proxy

The UI reaches the API **only** through a Nitro server-side proxy at `/api/delibera/**`. That is not a
convenience — two defects force it:

1. `Delibera.Server` registers **no CORS policy**, so a browser calling the API origin directly is
   blocked on every request, GETs included.
2. `DebateMapper` builds the absolute `streamUrl` / `resultUrl` from `Scheme://Host` with no
   forwarded-header handling, so both point at the wrong host behind a proxy or an ingress.

### SSE is forwarded, not buffered

The proxy forwards the event stream with `sendStream` rather than collecting it. This is asserted
against a real socket: the upstream withholds its terminal event for 3 seconds, and the test **fails
if the first chunk has not arrived by then**.

A buffering proxy would pass every functional test and still deliver each round 150–200 seconds
late — which is exactly how long a debate runs.

Frontend hot reload is `nuxt dev` on the host against `http://localhost:5200`.

---

## The proxy bug that made every POST fail

`readBody` **parses** the request payload — JSON in, plain object out. `fetch` stringifies a
non-string body with `String(value)`, which produces the literal `"[object Object]"`.

The upstream therefore received invalid JSON and answered `400 Bad Request` with no field detail.
Only the GET paths worked; creating a debate, cancelling one and exporting were all unreachable
through the UI.

```ts
// before — every POST sends "[object Object]"
const upstreamInit = { method, headers: buildUpstreamHeaders(...) }

// after — objects are re-encoded, strings pass through untouched,
// and empty objects become undefined so a DELETE is not asked to
// validate a body it does not have
function encodeUpstreamBody(parsed: unknown): string | undefined {
  if (parsed === undefined || parsed === null) return undefined
  if (typeof parsed === 'string') return parsed
  if (typeof parsed === 'object' && Object.keys(parsed as object).length === 0) return undefined
  return JSON.stringify(parsed)
}
```

Three smaller defects shipped alongside it:

- **A debate that finished before the timeline attached showed no rounds.** Seeding ran only when the
  record was already inactive on mount; a debate opened right after creation arrives Running, and if
  it finished before the page settled the terminal event carries no rounds — the timeline stayed empty
  while the verdict panel was already populated. Seeding is now unconditional and dedupes by round
  number.
- **A missing debate was polled forever.** The fallback poll treated a 404 as transient. A 404 is
  terminal — the record is unknown or past its ~30 min eviction, and retrying hammers the API with a
  request that can never succeed. 5xx and unreachable still keep polling, because a debate may
  genuinely be running in another process after a restart.
- **The stream-state badge rendered unstyled.** `StatusBadge.vue` scopes its styles, so the `.badge`
  class used on the debate page never matched — producing an unstyled element and, with it, a
  malformed accessibility tree. Those styles now live on the page that uses them.

---

## Redis, actually wired up

`Delibera.Redis` shipped as a standalone package and **could never be switched on inside the server**.
`Delibera.Server.csproj` referenced only `Delibera.Core`, so `AddRedisDebateOrchestrator` was never
called and the server always ran `LocalDebateOrchestrator`. A `redis` service in compose would have
connected to nothing.

`Delibera.Server` now references `Delibera.Redis` and registers the Redis orchestrator — and,
optionally, the Redis result cache — when `Delibera:Redis:Enabled` is true.

**Off by default.** The default path resolves the same in-process orchestrator as before.

```json
{ "Delibera": { "Redis": { "Enabled": true, "ConnectionString": "localhost:6379" } } }
```

### What this buys, and what it does not

**Buys:** round events published to a Redis Stream (so any API instance can stream them over SSE),
shared debate state, and optional result caching.

**Does not buy: distributed turn execution.** `DebateWorkerService.ProcessMessageAsync` is an
explicit no-op placeholder, and the orchestrator still runs each debate in the process that accepted
it. **Do not read this as horizontal debate scaling.**

A bad connection string now fails at startup with a message naming the key, rather than silently
degrading — a deployment that quietly fell back would look distributed and not be.
`abortConnect=false` still covers transient ordering, so compose can start Redis and the server in
either order.

`DebateWorkerService` is deliberately **not** registered: it would spin an `XREADGROUP` poll loop
against a stream nobody publishes to.

---

## Docker Hub images

`techbuzzz/delibera-server` and `techbuzzz/delibera-webui`, published from `v*` tags by
`.github/workflows/publish-docker.yml` as `linux/amd64` + `linux/arm64`.
`deploy/docker-compose.hub.yml` pulls them with no build step:

```bash
curl -O https://raw.githubusercontent.com/techbuzzz/Delibera/main/deploy/docker-compose.hub.yml
docker compose -f docker-compose.hub.yml up -d
```

Set `DELIBERA_VERSION=10.5.2` to pin a specific release.

Ollama is **not** re-published — it keeps its `ollama` profile and its official upstream image.

---

## Verification

Measured on this release, not carried over from the previous one:

| Suite | Result |
|---|---|
| `dotnet build -c Release -warnaserror` | 0 errors, 0 warnings |
| `Delibera.Core.Tests` | 493 passing |
| `Delibera.Server.Tests` | 117 passing |
| `Delibera.Grpc.Tests` | 10 passing |
| **.NET total** | **620 passing, 0 failed, 0 skipped** |
| `nuxt typecheck` | clean |
| `nuxt build` | clean |
| `vitest run` | 34 passing across 4 files |

The 611 figure published with v10.5.1 remains correct for that tag; `Delibera.Server` gained 9
Redis-orchestration tests in this release.

CI runs all of the above on every pull request — build, test and Web UI checks are separate required
gates, not one job that can hide a failure in another.

---

## Upgrading

1. **Pull.** `git pull` on `main`; the tag is `v10.5.2`.
2. **Expect localhost-only ports.** If anything reaches the server from another machine, add an
   authenticating reverse proxy and bind its port explicitly.
3. **Expect the new transitive package.** `Delibera.Redis` and `StackExchange.Redis` arrive with
   `Delibera.Server`. Nothing to configure unless you want to enable Redis, and it stays off unless
   `Delibera:Redis:Enabled` is true.
4. **If you use the Web UI, you are getting the POST fix** — if creating a debate through the browser
   previously returned 400, it will work now.