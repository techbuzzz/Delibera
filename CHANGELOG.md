# Changelog

All notable changes to **Delibera** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — opt-in CORS for static browser clients

`Delibera:Server:Cors:AllowedOrigins` registers a named CORS policy listing the browser origins
permitted to call the API cross-origin, and `UseCors` is wired only when that list is non-empty.

**Off by default, and the default is the point.** The Web UI reaches the API through its own
same-origin BFF route and never needs CORS. The absent header is also what stops an arbitrary web
page from *reading* a response — a cross-origin `fetch` still fires the request without it, it just
cannot see the answer. Turning this on globally would have added a browser-side CSRF vector to an
API that is unauthenticated, unthrottled, and spends real credits per debate.

Set it only for a deliberately static client — the GitHub Pages build of the Web UI, which has no
Nitro server and therefore no BFF route. In compose:

```yaml
environment:
  DELIBERA_CORS_ORIGIN: https://techbuzzz.github.io
```

Origins are matched exactly; there is no wildcard switch. `AllowCredentials` together with `*`
is rejected at startup with a message naming the config key, rather than by the framework with one
that names nothing. 8 new tests, including that the default path registers no CORS service at all.

## [10.5.2] - 2026-10-09

**Contains behaviour changes.** Two are breaking for existing deployments: published ports now
bind to `127.0.0.1` instead of `0.0.0.0`, and `Delibera.Server` now pulls `Delibera.Redis` +
`StackExchange.Redis` transitively on NuGet. Both are documented below under *Security* and
*Changed*. The breaking changes are real but deliberate; the patch number is retained because the
tag and the merge history already declare this release as v10.5.2.

### Added — Web UI (`Delibera.WebUI`, Nuxt 4)

A browser front end for the debate workflow, shipped as a second container alongside
`delibera-server`. Scope: list debates, create one from a registered template, watch rounds
arrive live, read the verdict with token stats, export Markdown, cancel a running debate.

The UI talks to the API **only through a Nitro server-side proxy** (`/api/delibera/**`). That is
not a convenience: `Delibera.Server` registers no CORS policy, so a browser calling the API
origin directly is blocked on every request, GETs included. Proxying also sidesteps a second
defect — `DebateMapper` builds the absolute `streamUrl`/`resultUrl` from `Scheme://Host` with no
forwarded-header handling, so they point at the wrong host behind a proxy or ingress.

SSE is forwarded with `sendStream` rather than buffered. The suite asserts this against a real
socket: the upstream withholds its terminal event for 3 s, and the test fails if the first chunk
does not arrive before then. A buffering proxy would pass every functional test and still deliver
each round 150–200 s late.

`docker compose up` now starts `delibera-webui` alongside the rest. Frontend hot reload is
`nuxt dev` on the host against `http://localhost:5200`.

### Added — Redis is now an opt-in runtime feature of `Delibera.Server`

`Delibera.Redis` shipped as a standalone package and could never be switched on inside the server:
`Delibera.Server.csproj` referenced only `Delibera.Core`, so `AddRedisDebateOrchestrator` was never
called and the server always ran `LocalDebateOrchestrator`. A `redis` service in compose would have
connected to nothing.

`Delibera.Server` now references `Delibera.Redis` and registers the Redis orchestrator and,
optionally, the Redis result cache when `Delibera:Redis:Enabled` is true. Off by default — the
default path resolves the same in-process orchestrator as before.

What this buys: round events published to a Redis Stream (so any API instance can stream them over
SSE), shared debate state, and optional result caching. **What it does not buy: distributed turn
execution.** `DebateWorkerService.ProcessMessageAsync` is an explicit no-op placeholder, and the
orchestrator still runs each debate in the process that accepted it. Do not read this as horizontal
debate scaling.

A bad `Delibera:Redis:ConnectionString` now fails at startup with a message naming the key, rather
than silently degrading — a deployment that quietly fell back would look distributed and not be.
`abortConnect=false` still covers transient ordering, so compose can start Redis and the server in
either order. `DebateWorkerService` is deliberately not registered: it would spin an `XREADGROUP`
poll loop against a stream nobody publishes to.

### Added — Docker Hub images

`techbuzzz/delibera-server` and `techbuzzz/delibera-webui`, published from `v*` tags by
`.github/workflows/publish-docker.yml` as `linux/amd64` + `linux/arm64`. `deploy/docker-compose.hub.yml`
pulls them with no build step:

```bash
docker compose -f deploy/docker-compose.hub.yml up -d
```

Ollama is not re-published; it keeps its `ollama` profile and its official upstream image.

### Security — published ports now bind to 127.0.0.1

**This is a behaviour change.** Every published port in `docker-compose.yml` was previously bound
to `0.0.0.0`; all of them now bind to `127.0.0.1`.

The API is **unauthenticated and unthrottled**, and a debate spends real LLM credits. That was
already true, but publishing the images makes it trivially reachable. Localhost-only binding is the
enforcement; the README, README-RU and `.env.example` now state plainly that an authenticating
reverse proxy is required before this stack is exposed to any network you do not control.

A host currently reaching `Delibera.Server` from another machine on its LAN will stop being able to
after this upgrade. Bind explicitly if that is intended.

### Changed — `Delibera.Server` now depends on `Delibera.Redis` on NuGet

The ProjectReference flows into the nuspec, so existing consumers pull `Delibera.Redis` and
`StackExchange.Redis`. This is deliberate and accepted rather than hidden with `PrivateAssets="all"`,
which would remove `AddRedisDebateOrchestrator` from the published package — the one thing the
reference exists to expose. Both packages ship in the same GA matrix at the same version.

### Fixed — every POST through the Web UI proxy returned 400

The Nitro proxy passed the request body to `fetch` as-is. `readBody` **parses** the payload — JSON
in, plain object out — and `fetch` stringifies a non-string body with `String(value)`, which yields
the literal `"[object Object]"`. The upstream therefore received invalid JSON and answered `400 Bad
Request` with no field detail, for every POST. Creating a debate, cancelling one and exporting were
all unreachable through the UI; only the GET paths worked.

The body is now re-encoded before the upstream call: a parsed object is serialised with
`JSON.stringify`, a string is passed through untouched (`readBody` returns raw text for non-JSON
content types, and re-encoding it would corrupt it), and an empty object becomes `undefined` — a
body-less DELETE should not ask the API to validate a body.

### Fixed — a debate that finished before the timeline attached showed no rounds

Seeding ran only when the record was already inactive on mount. A debate reached by clicking through
from the list arrived Completed and was seeded; one opened right after creation arrived Running, the
stream was opened, and if the debate finished before the page settled the terminal event carries no
rounds — leaving the timeline empty while the verdict panel was already populated. Seeding is now
unconditional and dedupes by round number, so both paths are covered.

### Fixed — a missing debate was polled forever

The fallback poll treated a 404 as transient and kept retrying. A 404 is terminal: the record is
either unknown or past its ~30 min eviction, and polling it forever hammers the API with a request
that can never succeed. 404 now stops the poll; 5xx and unreachable still keep it, because a debate
may genuinely be running in another process after a restart.

### Fixed — the stream-state badge rendered unstyled

`StatusBadge.vue` scopes its styles, so the `.badge` class used on the debate page was never
matched: the element rendered with no styling and, with it, a malformed accessibility tree. The
styles are now declared on the page that uses them.

### Added — document head metadata for accessibility

`htmlAttrs.lang`, a real title, description and viewport — without all three Lighthouse fails the
document and screen readers get no document language.

Tests: 620 passing (493 Core + 117 Server + 10 gRPC contract), 0 failed, 0 skipped, plus 34 Web UI
tests. The release build is clean under `-warnaserror`. The 611 figure published with v10.5.1
remains correct for that tag; Server gained 9 Redis-orchestration tests in this release.

## [10.5.1] - 2026-10-07

Closes all six open GitHub issues (#11, #13, #14, #15, #16, #18) and the two findings that were
tracked only in `docs/TASKS/`. **No breaking changes** — every addition is opt-in, and no member
was added to an existing public interface.

### Fixed — the CLI could not authenticate to Ollama Cloud

`delibera run` built its Ollama provider as `new OllamaProvider(endpoint)`, with no API key, while
the Yandex branch read one. Against a local daemon that is fine and invisible; against
`api.ollama.com` every call returned **401 Unauthorized**. The key is now read from the model's
`ApiKey` or from `Delibera:Ollama:ApiKey`, matching the Yandex branch.

### Fixed — per-call cost accounting ran on every debate whether or not anyone wanted it

Recording a member call was unconditional: each call concatenated the system and user prompt and
ran the token counter over both the prompt and the response. The prompt grows every round as history
accumulates, so a debate paid a scanning cost quadratic in its own length — and then threw the
result away, because `CostEstimate` is published only when a gate, limiter or price registry is
configured.

Measured against v10.5.0 with the same harness and workload:

| | Median | Allocations |
|---|---|---|
| v10.5.0 | 0.634 ms | 3 735 640 B |
| v10.5.1 before this fix | **1.404 ms** (+121%) | **5 034 080 B** (+35%) |
| v10.5.1 after | 0.679 ms | 3 736 000 B (+0.01%) |

Two changes: accounting now runs only when `DebateExecutionOptions.CostTrackingEnabled`, and the
per-member admission check stopped being an `async ValueTask` — its state machine was allocated on
every call even when the method returned immediately, which is most of what remained.

The improvement that survived: three index runs of the same document now leave the same points as one,
instead of three times as many — 120 points rather than 360 against live Qdrant.

### Fixed — the pgvector backend could not write or search

`PgVectorStore` bound the embedding with `AddWithValue(new Vector(v))`. That overload takes `object`, so
the `Vector` was boxed and Npgsql had no type to map; the first upsert threw

```
InvalidCastException: Writing values of 'Pgvector.Vector' is not supported for parameters having no
NpgsqlDbType or DataTypeName
```

Declaring `NpgsqlDbType.Unknown` fails the same way — `UseVector()` registers a mapping only for the
Npgsql version the `Pgvector` package was built against, and this project pairs `Pgvector` 0.3.2 with
Npgsql 10. The embedding now travels as pgvector's own text form (`[0.5,1,-2]`) with an explicit
`NpgsqlDbType.Text` and a `::vector` cast applied by PostgreSQL, which any pgvector release understands.
Formatting is culture-invariant: a decimal comma would render `[0,5]`, which parses as two zeros and
corrupts the vector silently instead of failing.

Found by running the store against a real `pgvector/pgvector:pg16` — the unit tests covered it through
fakes. `PgVectorStore` is advertised as a supported vector store and **had never been exercised against
a real database**; anything configured with `Delibera:Rag:ProviderType = PgVector` could not have
worked. Both write and search were affected and both are fixed. No migration is required, because no row
could previously have been written.

### Fixed — the tool audit trail reported every call as failed

A live run reported `1 tool call(s) observed, 0 succeeded … error="The tool produced no result."` even
though the tool had demonstrably run.

The call and its result arrive in *different* round-trips: the request leaves the provider as a
`FunctionCallContent`, the middleware invokes the tool afterwards, and the result only comes back on the
**next** request. The recorder looked for the pair inside a single response, so a result could never be
found and every call looked dropped. Calls are now held pending by `CallId` and settled when a matching
result appears in an inbound conversation; anything still pending when the loop ends is flushed as having
produced nothing. Two regression tests in `ToolBridgeTests` pin both halves.

### Fixed — an unknown tool name was reported as a success

The middleware reports an unresolvable function as an error *value*, not an exception, so testing
`Exception is null` passed. What is unambiguous is the catalogue: a name that was never offered cannot
have run, whatever came back. Success now requires the result, no exception, **and** the tool being in
the catalogue.

### Fixed — vector-store indexing was not idempotent (W2-15)

`BaseRagProvider` assigned `Guid.NewGuid()` as every point id. Both concrete stores treat the id as
an upsert key — Qdrant as the point id, pgvector as `INSERT … ON CONFLICT (id) DO UPDATE` — so a
fresh id meant re-indexing never matched an existing row and appended a second copy of the corpus.
Measured in 10.5.0: three runs over 24 unique chunks left 72 points, and every later search paid
for the duplicates.

The id is now derived from the chunk's identity:

- with a stable source: `(collection, source_path, chunk_index)` — **position-derived**, so an
  edited chunk overwrites its own point instead of orphaning the previous one, which is what
  actually fixes the reported defect;
- without one: `(collection, content hash)`, which at least makes re-indexing identical text a no-op.

The result is a parseable GUID on purpose. Both stores silently substitute a random GUID when the
supplied id fails to parse, which would reintroduce exactly the duplication this removes; a test
asserts every emitted id parses.

> ⚠️ **Migration.** Collections written by 10.5.0 or earlier hold random-id points that re-indexing
> will never replace. Delete the collection through `IVectorStore.DeleteCollectionAsync` and
> re-index once. `IndexFileAsync` logs a warning when the pre-existing point count exceeds what the
> current document contributes — that is the signature of an unmigrated collection.

### Added — function calling / tool use (#14)

`IToolProvider` and `AIFunction` tools that members may call while forming a response.
`DebateRound.ToolCalls` and `DebateResult.ToolCalls` record what was actually invoked.

Two transports, one log type:

- **Native**, when the member's provider wraps a real `IChatClient` — `FunctionInvokingChatClient`
  drives the loop, so requests and results travel as real message content.
- **Marker**, `[[TOOL: name {json}]]`, for every other provider. This is not a stylistic choice:
  the adapter returned by `AsChatClient()` calls `ChatAsync`, ignores `ChatOptions.Tools`, and
  flattens every inbound message through `message.Text` — and function-call content has no text. The
  middleware never sees a request and never delivers a result, so function invocation on top of that
  adapter is inert. The marker protocol mirrors the Operator's existing `[[OPERATOR: …]]` convention
  and needs no provider changes.

Loop bounded by `MaxToolIterations` (default 3) so a model that keeps requesting tools cannot spin.

Ships `FileSystemToolProvider` (rooted to a configured directory, rejects traversal, size-capped),
`HttpToolProvider` (host allow-list, refuses plain HTTP by default) and `McpToolProvider`. Granting
members tools grants them the tool's authority; neither provider is safe by accident.

### Added — cost gates and rate limiting (#18)

`ICostGate`, `IRateLimiter`, `IModelPricingRegistry`, with `BudgetCostGate`,
`TokenBucketRateLimiter`, `ModelPricingRegistry` and a thread-safe `CostLedger`.
`DebateResult.CostEstimate` reports spend per member, flagged `IsEstimate` whenever a model had no
registered price.

A denial does **not** throw. The debate returns a degraded result carrying the spend so far, because
a ceiling that throws takes the debate down and leaves the operator with no record of what was
already spent. Configured through `WithCostLimit` / `WithRateLimit` / `WithPricingRegistry`, and
through `CouncilOptions` for the DI path.

Configuring a ceiling **without** a price list now logs a warning at construction: with no prices
every call bills zero, the gate compares zero against the limit and never denies, and the caller is
left with a ceiling that is configured, logged and completely inert.

### Added — `DebateResult.Diff` (#15)

`DebateResultExtensions.Diff(other)` compares two runs and reports how far apart they are: a
word-level diff of every changed member response, a similarity score for the verdicts, and explicit
lists of rounds or members present on only one side. Markdown and self-contained HTML export.

Rounds match on `RoundNumber` and members on display name, never on list position — comparing a
four-round run against a three-round run reports the missing round instead of silently shifting every
later comparison onto the wrong response.

`TextSimilarity` is now public and takes an explicit maximum length. The diff passes
`int.MaxValue`: inheriting the 1024-character stalemate cap would report two long verdicts differing
only in their final sentence as identical.

### Added — `delibera` CLI (#16)

`run`, `resume`, `compare` and `benchmark` on `System.CommandLine`.

`compare` diffs two saved results; `benchmark` reports the distribution across runs, because wall
time is dominated by how much the models choose to write and a single run says nothing.
`resume` reconstructs a result from a checkpoint's completed rounds — it reports progress rather than
continuing the debate, and is marked `IsCompleted = false` so it cannot be mistaken for a verdict.

### Added — gRPC transport (#11)

`Delibera.Grpc` and `Delibera.Grpc.Client`, generated from `Protos/delibera.proto`, which is derived
from the contracts the server already validates. Both sides compile the same proto independently —
referencing the server project *and* generating locally produces two copies of every message type and
a caller holding two nominally different `DebateResponse` types.

The service layer sits **on top of** `IDebateOrchestrator` rather than beside it, so distributed
execution and result caching behave identically over gRPC. A parallel pipeline would silently ignore
`WithOrchestrator` and `WithCache`.

Server streaming delivers every round exactly once and then exactly one terminal event, guarded by
`terminalWritten`. Iteration is a plain `await foreach` — one `MoveNextAsync` in flight at a time —
because the SSE writer shipped a bug for precisely that reason: it compared a winner against a second
`ValueTask.AsTask()` call, which returns a *different* `Task` instance, so every genuine event was
misread as a heartbeat and a debate streamed zero rounds.

Status ordinals are pinned explicitly in the proto and covered by a test: REST serialises the same
states as strings, so nothing in either build would stop the two transports from disagreeing.

### Changed — NuGet GA matrix (#13)

`Delibera.Server` and `Delibera.Redis` are now publishable, so the GA matrix is `Delibera.Core`,
`Delibera.Server` and `Delibera.Redis`. `publish-nuget.yml` packs all three and now **fails** if any
package is missing its README or icon.

`Delibera.Server` needed `<IsPackable>true</IsPackable>`: `Microsoft.NET.Sdk.Web` sets it false because
the Web SDK's default output is an app, not a library. Both new packages carry their own `README.md`
and `icon.png` — `PackageIcon` and `PackageReadmeFile` pointing at `..\..\` do not resolve during pack.

`Delibera.Grpc` and `Delibera.Grpc.Client` carry identical metadata and are one workflow line away
from publishing; they are deliberately **not** in the matrix yet.

### Documentation

- `docs/performance-measurements.md` labelled a row `GET /health`; the mapped route is
  `/api/v1/health`.
- `docs/TASKS/README.md` now carries the measured test count rather than one carried forward from a
  previous release, and includes W2-10…W2-16, which were tracked only in `W2-performance-core.md`.
- W4-08 and W4-10 were marked done. Verifying them first: W4-08 is genuinely fixed (`wget` is
  installed explicitly and `curl` — not `wget` — is purged afterwards), but the acceptance criterion
  "a built image reports healthy" still needs `docker compose up`, so it stays unchecked. W4-10 is
  **partial**, not done: the SSE writer is fixed, but `Channel.CreateUnbounded` remains in
  `LocalDebateOrchestrator.cs:251` and `RedisDebateOrchestrator.cs:419`.

### Verification

611 unit tests, **611 passing** (493 Core + 108 Server + 10 gRPC contract), 0 failed, 0 skipped.
`dotnet build Delibera.slnx -c Release -warnaserror` is clean (0 warnings, 0 errors) across all eight
projects.

The 10.5.0 page for `Delibera.Core` states *514 unit tests (406 Core + 108 Server)*. That figure was
measured at 10.4.0; 10.5.0 shipped with 10.4.0's release notes still on its page while the real count
had moved on. It is corrected here, on the 10.5.1 page — the 10.5.0 page cannot be edited.

### Still open, carried forward

- **W2-13** — `WithCache` is missing from `ICouncilBuilder`. Adding it as an abstract member would be
  a source break, which 10.5.x does not take; documented rather than patched.
- **W2-14** — on a cache hit, `TotalDuration` reports the cached debate's duration, not the caller's
  wait time (measured: 0.0 s actual vs 189.0 s reported). A product decision, not a patch.
- **Vector pruning** — a document that *shrinks* leaves its tail chunks behind. Closing that needs a
  filter-delete on `IVectorStore`, which is breaking under the no-breaking-change rule.
- **W4-10 channels** — still unbounded, so a consumer that stops draining grows memory without limit.
  Bounding them means a defined full-channel behaviour, which is a behavioural change left for a
  separate decision.

## [10.5.0] - 2026-10-06

Multi-model deliberations measured end-to-end against Ollama Cloud for the first time. That
surfaced five defects, two of which made headline features non-functional. Every number below is
from a real run; see [docs/performance-measurements.md](docs/performance-measurements.md) for the
full evidence and for the claims these results contradict.

### Fixed

- **Context compression never ran.** `CouncilExecutor.CompressTextAsync` was public API that nothing
  in the pipeline called, `TokenStats` and `CompressionLogs` were never assigned, and ten measured
  runs with compression enabled reported 0.00% saved at 0 ms overhead while context grew past
  10,000 tokens per round. The compressor is now attached through `DebateExecutionOptions` and
  applied to each round prompt above a `CompressionThresholdTokens` (default 1,200) floor, with
  every attempt logged. Measured saving is now **11.7–12.1%**.

  > This is far below the **30–70%** claimed in the README and compression docs. The wiring is what
  > made the feature work at all; the strategy's yield is about a sixth of what was advertised.

- **`OllamaProvider` could not talk to Ollama Cloud in its default configuration.** The provider
  defaulted to `maxOutputTokens = -1` and sent that as `num_predict`, which the endpoint rejects:
  `OllamaException: max_tokens must be positive, got: -1`. Every request failed before generating a
  token. A non-positive cap is now sent as a `null` `NumPredict`, which OllamaSharp omits from the
  request so the server applies its own default. Positive caps pass through unchanged.

- **`DebateResult.TotalDuration` was negative on every debate.** `StartedAt` relied on a property
  initialiser that runs inside `DebateResultBuilder.Build()` — after `MarkCompleted()` had already
  stamped `CompletedAt` — so `CompletedAt - StartedAt` came out below zero. Start time is now
  captured when the builder is created.

- **A failed participant's error text was fed to the Chairman as an opinion.** `DebateScenario`
  substituted `$"[ERROR: {ex.Message}]"` as the member's response, so an error entered the
  transcript, was read as a viewpoint, and produced a verdict that looked complete while being
  synthesised from a partial council. Failed members are now omitted from the round and recorded on
  the result.

- **An empty response gave no diagnostic.** `InvalidOperationException("Empty response from model
  '...'")` did not distinguish the two causes, which behave completely differently.

### Added

- **`DebateResult.FailedMembers`** (`IReadOnlyList<MemberFailure>`) and **`DebateResult.IsDegraded`**.
  A non-empty list means the verdict came from a partial council; nothing downstream has to infer it
  from text.
- **`MemberFailure`** record — round number and name, role, display name, model, and the error, with
  a `ToString()` suited to execution logs and report rows.
- **`OllamaEmptyResponseException`** carrying `DoneReason` and `ReasoningChars`, with
  **`BudgetConsumedByReasoning`** separating "the generation budget went into reasoning" from "the
  model returned nothing at all". Its message states which case occurred and what to do about it.
- **`OllamaProvider(enableThinking:)`** (default `false`) — sends `Think` explicitly instead of
  leaving it unset, so a reasoning model does not silently consume the answer budget.
- **`OllamaProvider(retryOnBudgetExhaustion:)`** (default `true`) — retries once with a larger
  budget when a call ends `done_reason: length` with no content and reasoning present. This is a
  budget repair, not a transport retry, so it sits inside the Polly-wrapped operation.
- **`DebateExecutionOptions.ContextCompressor` / `ContextCompressionOptions` /
  `ContextCompressionCache` / `CompressionLogs` / `CompressionThresholdTokens`** — how the
  executor reaches the compressor without widening `IDebateStrategy`.
- **Four regression tests** (`ContextCompressionWiringTests`) pinning that a configured compressor is
  invoked, that savings reach the result, that an unconfigured run invents nothing, and that a
  compressor returning *more* text is not trusted.

### Known issues (reported, not fixed)

- **`DebateResult.TotalDuration` on a cache hit** reports the cached debate's duration, not the time
  the caller waited. Measured: a cached lookup served in 0.0 s reported 189.0 s. `CacheHit` and
  `CachedAt` are the only signals distinguishing the two. Whether the property should carry
  provenance or wait time is a product decision, so the behaviour is documented rather than changed.
- **`WithCache(CacheBehavior, IDebateCache)` exists only on the concrete `CouncilBuilder`**, not on
  `ICouncilBuilder`. The interface exposes only `WithCacheBehavior`, documented as picking the cache
  up from DI, so an interface-typed consumer cannot supply a backend.
- **Vector-store indexing is not idempotent.** `IndexFileAsync` appends unconditionally: three runs
  over the same 24 chunks left 72 points, diluting retrieval with exact duplicates. A deployment
  that indexes on startup degrades its own search quality over time.
- **Documented compression savings (30–70%) are inaccurate.** Measured 11.7–12.1%.

## [10.4.0] - 2026

### ⚠️ Breaking Changes (W3-07 — async round callback)

`IDebateStrategy.ExecuteAsync` accepts an awaitable round callback. Implementations and callers
must update; everything else in this release is backwards compatible.

| Before | After |
|--------|-------|
| `Action<DebateRound>? onRoundCompleted` | `Func<DebateRound, CancellationToken, ValueTask>? onRoundCompleted` |
| `onRoundCompleted?.Invoke(round);` | `if (onRoundCompleted is not null) await onRoundCompleted(round, ct).ConfigureAwait(false);` |

The public `CouncilExecutor.OnRoundCompleted` **event** is unchanged and remains a
fire-and-forget `Action<DebateRound>` — existing `+=` subscribers are unaffected.

```csharp
// Before — synchronous callback
public Task<DebateResult> ExecuteAsync(
    IReadOnlyList<CouncilMember> members, PromptContext context, CouncilMember? chairman,
    KnowledgeKeeper? knowledgeKeeper, Operator? @operator, DebateExecutionOptions executionOptions,
    int maxRounds = 4, float temperature = 0.7f,
    Action<DebateRound>? onRoundCompleted = null, CancellationToken ct = default)
{
    // ...
    onRoundCompleted?.Invoke(round1);
    return Task.FromResult(result);
}

// After — awaitable callback
public async Task<DebateResult> ExecuteAsync(
    IReadOnlyList<CouncilMember> members, PromptContext context, CouncilMember? chairman,
    KnowledgeKeeper? knowledgeKeeper, Operator? @operator, DebateExecutionOptions executionOptions,
    int maxRounds = 4, float temperature = 0.7f,
    Func<DebateRound, CancellationToken, ValueTask>? onRoundCompleted = null, CancellationToken ct = default)
{
    // ...
    if (onRoundCompleted is not null)
        await onRoundCompleted(round1, ct).ConfigureAwait(false);
    return result;
}
```

**Why.** A synchronous callback forced the executor to block on asynchronous work
(`CouncilExecutor`, per-round selector consult and checkpoint write). One thread-pool thread was
pinned for the duration of a file/network checkpoint write on **every round** of **every**
in-flight debate, and `OperationCanceledException` was repackaged as `AggregateException`.
Awaiting the callback removes the blocked thread and propagates cancellation unchanged.

Custom strategies that invoke the callback from a synchronous helper must become async and
await the helper. Because the callback is now awaited before the next round is produced, the
existing backpressure guarantee is unchanged — it is now enforced by `await` rather than by
blocking.

### Fixed

- **SSE debate streaming dropped every round.** `SseDebateStreamWriter`'s heartbeat pump
  compared `Task.WhenAny(...)`'s winner against a *second* `next.AsTask()` call.
  `ValueTask.AsTask()` on a compiler-generated async iterator returns a different `Task`
  instance on the second call, so the comparison was false even when an event had already
  arrived — every genuine event was misclassified as a heartbeat and skipped, and a debate
  streamed **zero** rounds.
- **SSE debates that completed mid-stream raised `NotSupportedException` after the terminal
  event.** After a heartbeat pulse the loop `continue`d and called `MoveNextAsync` again
  while the previous move was still in flight, then left that move pending when the method
  `return`ed on a terminal event. An async iterator forbids a second concurrent
  `MoveNextAsync` and forbids `DisposeAsync` while one is in flight, so a client could
  receive a successful `debate-completed` event and an error on the same connection. One
  `MoveNextAsync` is now created per iteration and reused across pulses, the winner is
  compared against that single task by reference, and any pending move is settled before
  disposal. Terminal events are still delivered exactly once, keep-alive comments are still
  emitted, and the reconnect hint is still the first thing on the wire.
- **`Microsoft.OpenApi` 3.10.2 could not build.** `Microsoft.AspNetCore.OpenApi` 10.0.12
  still emits `Example = ...` assignments against `IOpenApiMediaType.Example`, which is
  read-only in Microsoft.OpenApi 3.x, producing `CS0200` inside generated
  `OpenApiXmlCommentSupport` code. Pinned to **2.12.2** in `Delibera.Server`: the last
  published 2.x, and the floor `Microsoft.AspNetCore.OpenApi` 10.0.12 requires
  (`[2.12.0, 3.0.0)`). A literal `2.7.5` pin does not restore — it is `NU1605` (detected
  package downgrade 2.12.0 → 2.7.5).
- **`Qdrant.Client` 1.19.0 marked `QdrantClient.SearchAsync` obsolete**, which is a build
  error under `-warnaserror`. `QdrantVectorStore` now calls `QueryAsync` with the same
  collection, query vector, limit, score threshold and cancellation token.
  `float[] → VectorInput → Query` is written as two statements because C# will not chain
  two user-defined implicit conversions.
- **`ModelContextWindowRegistry` lookups** resolved whichever pattern the frozen dictionary
  happened to enumerate first. A tagged name such as `llama3.2:7b` matches both `llama3.2`
  (131072) and `llama3` (8192); the correct answer depended on authoring order rather than on a
  rule. Lookup is now exact-pattern-first via the frozen dictionary, then longest-matching-
  substring via a pattern-ordered index. `Freeze()` is now an eager warm-up that does not change
  results. 25 new tests pin the contract.
- **`ModelContextWindowRegistry`** built its `FrozenDictionary` via the parameterless
  `ToFrozenDictionary()`, which falls back to `EqualityComparer<string>.Default` — ordinal, not
  the case-insensitive comparer the source dictionaries use. The comparer is now passed
  explicitly.
- **`TextSimilarity`** doc comment claimed pooled fallback buffers that the code did not
  implement; it allocated two `int[n + 1]` rows per call.

### Performance

- **`DebateCacheKeyGenerator`** no longer materializes the entire knowledge base on the heap in
  order to UTF-8 encode it. The buffer is `ArrayPool`-rented with an exact byte count, matching
  the pattern already used in `CompressionCache`. The cache key value is unchanged.
- **`TextSimilarity`** Levenshtein rows longer than 128 characters now come from
  `ArrayPool<int>` instead of a fresh `int[n + 1]` each. At the 1024-character
  `MaxComparedLength` bound this removes ~8 KiB of Gen0 garbage per comparison on a per-round
  O(n²) loop.
- **`ModelContextWindowRegistry`** lookups no longer enumerate the entire frozen collection for
  every probe; the pattern-ordered index stops at the first (most specific) match.

### Verification

514 unit tests discovered, **514 passing** (406 Core + 108 Server), 0 failed, 0 skipped. The
five SSE failures carried since 10.3.x are fixed. `dotnet build Delibera.slnx -c Release
-warnaserror` is clean (0 warnings, 0 errors) and a forced restore reports no NU1605 /
NU1608 / NU1701.

---

## [10.3.0] - 2026

### ⚠️ Breaking Changes (P-01)

| Removed | Replacement |
|---------|-------------|
| `Moderator` static class | `Chairman` |
| `ICouncilBuilder.SetModerator(CouncilMember)` | `ICouncilBuilder.SetChairman(CouncilMember)` |
| `ICouncilBuilder.SetModerator(string, ILLMProvider, string?)` | `ICouncilBuilder.SetChairman(string, ILLMProvider, string?)` |
| `IDebateStrategyWithOptions` interface | `IDebateStrategy` (full signature with `DebateExecutionOptions`) |
| `IDebateStrategy.ExecuteAsync` overload without `DebateExecutionOptions` | Use the `DebateExecutionOptions` overload (pass `DebateExecutionOptions.Default`) |
| `ILLMProvider.GetModelCapabilitiesAsync` default `null` return | Implement explicitly; return `ModelCapabilities.Unknown(model)` when unknown |
| `ModelCapabilities?` (nullable) return type | `ModelCapabilities` (non-nullable); check `caps.IsUnknown` instead of `caps is null` |
| `RagProviderFactory` class | `VectorStoreFactory` |
| `IRagProviderFactory` interface | `IVectorStoreFactory` |

### Changed

- **`IDebateStrategy.ExecuteAsync`** now requires a `DebateExecutionOptions` parameter. The legacy overload without `DebateExecutionOptions` has been removed. Implement `IDebateStrategy` with the full signature; pass `DebateExecutionOptions.Default` when calling from code that doesn't need custom options.
- **`ILLMProvider.GetModelCapabilitiesAsync`** now returns `ModelCapabilities` (non-nullable) instead of `ModelCapabilities?`. Providers that cannot introspect capabilities should return `ModelCapabilities.Unknown(modelName)`. Callers should check `caps.IsUnknown` instead of `caps is null`.
- **`ModelCapabilities`** now has an `IsUnknown` property for checking whether the instance represents unknown capabilities.
- **`WeightedVotingStrategy.ResolveWeight`** now falls back to `ballot.Weight` when a member is not in `MemberWeights`, instead of the constructor's `defaultWeight`. This makes per-ballot weights work as documented.

### Added (S-01 — Distributed Debates)

- **`IDebateOrchestrator`** interface with `ExecuteAsync`, `EnqueueAsync`, `GetStatusAsync`, `StreamAsync`, `CancelAsync` — abstracts debate execution from the transport layer.
- **`LocalDebateOrchestrator`** — default in-process implementation wrapping `CouncilExecutor`, using `Channel<DebateRound>` for SSE streaming.
- **`RedisDebateOrchestrator`** — distributed implementation in `Delibera.Redis` project, publishing round events to Redis Streams, persisting state in Redis hashes.
- **`DebateHandle`**, **`DebateOrchestrationStatus`**, **`DebateRoundEvent`** — core types for the orchestration layer.
- **`DebateWorkerService`** — background service consuming jobs from Redis Streams.
- **`RedisOrchestratorOptions`** — configuration for Redis connection, stream keys, consumer groups.
- **`RedisOrchestratorExtensions.AddRedisDebateOrchestrator()`** — DI extension to swap `LocalDebateOrchestrator` for Redis.
- **`DebateOrchestrationService`** now delegates execution to `IDebateOrchestrator`, enabling local or distributed mode via DI.
- **`Delibera.Redis`** project (net10.0 class library) with `StackExchange.Redis 2.8.24` dependency.

### Added (S-03 — Result Caching)

- **`IDebateCache`** interface — `GetAsync`, `SetAsync`, `InvalidateAsync`, `ExistsAsync`.
- **`CacheBehavior`** enum — `Disabled`, `ReadWrite`, `ReadOnly`, `WriteThrough`, `Bypass`.
- **`DebateCacheKeyGenerator`** — deterministic SHA-256 cache key from debate inputs.
- **`InMemoryDebateCache`** — `IMemoryCache`-backed implementation with configurable TTL.
- **`FileDebateCache`** — JSON file-based implementation with TTL via file modification time.
- **`RedisDebateCache`** — `StackExchange.Redis`-backed implementation using `SETEX`.
- **`DebateResult.CacheHit`**, **`.CacheKey`**, **`.CachedAt`** — cache metadata on results.
- **`ICouncilBuilder.WithCacheBehavior(CacheBehavior)`** — per-debate cache control.
- **`ICouncilBuilder.WithCache(CacheBehavior, IDebateCache)`** — set both behavior and backend.
- **`CouncilExecutor`** now checks cache before execution and writes back on completion.
- DI extensions: **`UseInMemoryCache()`**, **`UseFileCache()`**, **`UseRedisCache()`**.
- **`debate.cache_hit`** OpenTelemetry counter metric emitted on cache hits.
- **`CacheHit`** / **`CacheKey`** fields added to `DebateResponse` DTO.

### Changed — Performance & Integrity (Phases 1–3)

- **Memory leak fixes:**
  - `LocalDebateOrchestrator`, `DebateOrchestrationService`, `RedisDebateOrchestrator` now evict completed entries after 30 minutes (Timer-based) to prevent unbounded memory growth.
  - `ProviderFactory` (created in server templates and `ScenarioBuilder`) is now `using var` — properly disposed.
  - `CompressionCache` and `FileDebateStore` now implement `IDisposable` (dispose `ReaderWriterLockSlim` / `SemaphoreSlim`).
- **Thread safety:**
  - `DebateRecord.Status` uses `volatile int` backing field with `Volatile.Read`/`Volatile.Write` for cross-thread visibility.
  - `RedisDebateOrchestrator.DebateEntry.Status` uses the same `volatile int` pattern.
  - `ProviderFactory.CachingFactory` uses `ConcurrentDictionary<string, T>` + `GetOrAdd` instead of `Dictionary` + manual `lock`.
- **Async correctness:**
  - `ConfigureAwait(false)` added to all `await` expressions in `Delibera.Core` and `Delibera.Redis` (~50+ sites) — library code must never capture `SynchronizationContext`.
  - Fire-and-forget `CancelAsync` in `DebateOrchestrationService` now uses `Task.Run` with try/catch + `_logger.LogWarning`.
- **Allocation optimisations:**
  - `SseDebateStreamWriter` uses `JsonSerializer.SerializeToUtf8Bytes` + byte-level writes instead of string-based SSE serialization.
  - `DebateCacheKeyGenerator.Generate` uses `SerializeToUtf8Bytes` instead of `Serialize` + `Encoding.UTF8.GetBytes`.
  - `ModelContextWindowRegistry` uses `FrozenDictionary<string, int>` / `FrozenSet<string>` for O(1) lookups; `Register`/`RegisterVisionPattern` invalidate the frozen cache.
  - `IDebateCache` and `IDebateStore` interfaces now return `ValueTask<T>` instead of `Task<T>` — synchronous backends avoid `Task` allocation.
  - `IDebateOrchestrator.GetStatusAsync` returns `ValueTask<DebateHandle?>`.
- **Micro-optimisations:**
  - `LevenshteinDistance` (in `CouncilExecutor` and `AdaptiveStrategySelector`) rewritten to single-row DP with `Span<int>` + `stackalloc` for strings ≤128 chars.
  - `TokenCounter` LRU lock replaced with `ReaderWriterLockSlim` for concurrent reads.
  - `RankedOption` changed from `sealed record` to `readonly record struct` — avoids heap allocation.
  - `DebateResult.ToMarkdown()` / `.ToStatisticsMarkdown()` / `.ToLogsMarkdown()` now use `StringBuilder` with capacity hints (4096 / 2048 / 2048).

### Fixed

- **`WeightedVotingStrategy.ResolveWeight`** now uses `ballot.Weight` as fallback instead of constructor `defaultWeight`, fixing `WeightedVoting_All_Zero_Weights_Throws`.

### Removed (Pre-merge Cleanup)

- **`DebateStatus.Paused`** — dead enum value never assigned; removed from `DebateResponse` contract.
- **`DebateOrchestrationStatus.Pending`** — dead enum value never assigned; removed from `DebateHandle` contract.
- **`DebateRecord._channel`**, **`RoundWriter`**, **`RoundReader`** — SSE channel moved to `IDebateOrchestrator.StreamAsync()`.
- **`SseDebateStreamWriter`** rewritten to consume `IDebateOrchestrator.StreamAsync()` instead of `DebateRecord.RoundReader`.
- **`DebateOrchestrationService.RunOrchestratorEnqueuedAsync`** rewritten from 200ms polling to event-driven `StreamAsync()`.

### Changed (Pre-merge Cleanup)

- **`DebateRecord.Label`** now defaults to `string.Empty` (was CS8618 warning).
- **`DebateResponse`** now includes a `Label` field mapped from `DebateRecord.Label`.
- **`DebateOrchestrationService.Enqueue`/`EnqueueScenario`** now call `_orchestrator.EnqueueAsync()` before streaming, ensuring the orchestrator registers the debate.
- **`SseDebateStreamWriter.WriteAsync`** signature changed to accept `IDebateOrchestrator` parameter (was `DebateRecord` + `HttpContext` + `CancellationToken` only).
- **`DebateEndpoints.StreamDebateAsync`** and **`ScenarioEndpoints.StreamScenarioAsync`** now receive `IDebateOrchestrator` via DI.
- **`FakeLLMProvider`** now implements `GetModelCapabilitiesAsync` (required by P-01 breaking change).
- Removed `TryGet` tests from `TemplateRegistryTests` (method removed in P-01).
- Fixed all CS1591 and CS1574 XML-doc warnings across `Delibera.Core`.

## [10.2.7] - 2026

Multi-Modal Council (F-06): bring images, diagrams, and documents into the
debate with zero forced dependencies in `Delibera.Core`.

### Added — F-06 Multi-Modal Council (Vision + Documents)

- **`Delibera.Core/Attachments/`** — new namespace with:
  - **`IFileContentReader`** — single extensibility point for document parsing.
    The core package ships **no** PDF/DOCX libraries; users register a reader
    per extension via `WithFileReader`.
  - **`FileReadResult`** — normalised output record (`SourcePath`,
    `TextContent`, `BinaryParts`, `Metadata`). Text + binary can both be
    non-null (e.g. PDF with embedded images).
  - **`BinaryAttachment`** — raw binary part (image bytes) with MIME type,
    ready for vision models via `Microsoft.Extensions.AI` `ImageContent`.
  - **`FileAttachment`** — file-attachment record (`FilePath`, `Description`).
  - **`FileContentReaderRegistry`** — per-extension registry, pre-populated
    with built-in readers. Always returns a reader (fallback for unknown
    extensions — never throws).
  - **`FileReaderDIEntry`** — DI entry record for `AddFileReader` extension.

- **Built-in readers** (zero external deps in `Delibera.Core`):
  - **`PlainTextFileReader`** — `.txt .md .markdown .json .xml .cs .yml .yaml .csv .html .htm .log .tsv`
  - **`ImageFileReader`** — `.png .jpg .jpeg .webp .gif .bmp` → `BinaryParts` with correct `MediaType`
  - **`FallbackFileReader`** — any unregistered extension → graceful placeholder text
  - **`DelegateFileContentReader`** — lambda adapter (no class needed)

- **`MemberCapabilities`** flags enum (`Text`, `Vision`) added to
  `Delibera.Core.Models`. `CouncilMember` gains `Capabilities` property and
  `SupportsVision` computed property.

- **`ModelContextWindowRegistry`** extended with vision detection:
  - `SupportsVision(string)` — substring match against known vision patterns
    (`llava`, `gemma3`, `llama3.2-vision`, `minicpm-v`, `gpt-4o`,
    `gpt-4-vision`, `claude-3`, `qwen-vl`, `internvl`, `pixtral`, `llama4`, …)
  - `GetCapabilities(string)` — combines context-window + vision detection
  - `RegisterVisionPattern(string)` — add custom patterns at startup
  - `GetVisionPatterns()` — snapshot of all registered patterns

- **`CouncilBuilder`** fluent API:
  - `WithAttachment(string filePath)` — attach a file (read lazily on debate start)
  - `WithAttachment(string filePath, string description)` — attach with description
  - `WithFileReader(string extension, IFileContentReader reader)` — register instance
  - `WithFileReader(string extension, Func<string, CancellationToken, Task<FileReadResult>> handler)` — register lambda
  - `AddMember(string, ILLMProvider, string, MemberCapabilities, string?)` — explicit capabilities overload
  - `AddMember(string, ILLMProvider, string?, string?)` — now auto-detects vision from model name

- **`ICouncilExecutor`** gains `Attachments` (`IReadOnlyList<FileAttachment>`) and
  `FileReaders` (`FileContentReaderRegistry`) surfaces.

- **`CouncilExecutor.ExecuteCoreAsync`** reads attachments via the registry and
  injects text content into the debate context's `KnowledgeContent`. Binary parts
  are available for future `Microsoft.Extensions.AI` `ImageContent` routing (the
  text-injection path is the foundation; full vision-model routing is a future
  enhancement that requires per-member message construction).

- **DI registration**: `ServiceCollectionExtensions.AddFileReader(extension, factory)`
  registers a `FileReaderDIEntry` singleton so DI-resolved `CouncilBuilder`
  instances can pick up custom readers automatically.

### Changed

- Bumped `Delibera.Core` package version `10.2.6` → `10.2.7`.
- `CouncilMember.AddMember(string, ILLMProvider, string?, string?)` now
  auto-detects `MemberCapabilities.Vision` from the model name.
- `<Description>` and `<PackageTags>` updated to mention multi-modal support.

### Compatibility

- **No breaking changes.** The new `AddMember` overload with `MemberCapabilities`
  is additive; existing callers get auto-detection for free. `WithAttachment`
  and `WithFileReader` are opt-in.

### Test summary

- 1 new test file, 48 new tests, **all 307 tests pass** (up from 259 in v10.2.6).
- Coverage: PlainTextFileReader roundtrip, ImageFileReader binary parts + MIME,
  FallbackFileReader placeholder, DelegateFileContentReader lambda,
  FileContentReaderRegistry (pre-registered, register instance, register lambda,
  fallback for unknown), MemberCapabilities flags, vision auto-detection
  (10 vision + 5 text-only model names), GetCapabilities combination,
  RegisterVisionPattern, CouncilBuilder wiring (WithAttachment, WithFileReader,
  AddMember with capabilities, auto-detect), end-to-end execution with text
  attachment, fallback reader, and custom reader.

### ConsoleApp

- `MultiModalExample` (`--multimodal`) — demos vision member + text member +
  attachments + custom `.pdf` reader (lambda style).

## [10.2.6] - 2026

A large feature release that delivers nine new capabilities across
observability, DX, enterprise decision support, persistence, and memory. Every
new feature is backward-compatible — existing call sites continue to work
unchanged. The release also adds nine new `--flag` demo entries to the
ConsoleApp (Streaming, Templates, Quick Wins, Telemetry, Adaptive Strategy,
Voting, Structured Output, Persistence, Agent Memory).

### Added — F-08 OpenTelemetry-style observability

- **`Delibera.Core/Telemetry/`** — new namespace with:
  - **`TelemetryOptions`** — `Enabled`, `ActivitySourceName`, `MeterName`, `ServiceVersion`.
    Bound from `Delibera:Telemetry` configuration section.
  - **`DeliberaActivitySource`** — centralised `System.Diagnostics.ActivitySource` facade
    with `static readonly` fields. Returns `null` activities when no listener is
    attached (the standard .NET zero-overhead pattern).
  - **`DeliberaMeter`** — `System.Diagnostics.Metrics.Meter` with histograms
    (`delibera.debate.duration`, `delibera.round.duration`), counters
    (`delibera.tokens.total`, `delibera.debates.completed`), and gauge
    (`delibera.compression.ratio`).
  - **`DeliberaTelemetry`** — high-level facade (`StartDebate`, `StartRound`,
    `StartMemberRespond`, `StartRagQuery`, `StartCompression`,
    `StartOperatorTask`, `StartChairmanSynthesize`, `StartChairmanOpen`,
    `RecordDebateCompleted`, `RecordRoundDuration`, `RecordTokens`,
    `RecordCompressionRatio`, `MarkSucceeded`, `MarkFailed`).
  - **`DeliberaActivityNames`** / **`DeliberaTelemetryTags`** — canonical span
    names and tag keys (single source of truth).
- **`CouncilBuilder.WithTelemetry(TelemetryOptions?)`** + delegate overload
  `WithTelemetry(Action<TelemetryOptions>)`.
- **`ICouncilExecutor.IsTelemetryEnabled`** + `DebateTimeout` + `LastStreamedResult`
  + `StrategySelector` + `VotingStrategy` + `StructuredOutputSerializer` +
  `StructuredOutputType` + `DebateStore` + `ResumeFromDebateId` + `AgentMemory`
  surfaces.
- `CouncilExecutor.ExecuteAsync` instruments: top-level `delibera.council.execute`
  span + per-round duration histogram + aggregate token counter + compression
  ratio gauge + debate-completed counter. `CompressTextAsync` wraps every
  compression call in a `delibera.compression` span.
- `GetInfo()` prints a new `── Telemetry ──` section when enabled.
- 22 unit tests; 0 NuGet deps added in `Delibera.Core` (uses in-box APIs).

### Added — F-10 Quick Wins Bundle (HTML / Timeout / Personas / Benchmark / Limit)

- **F-10a HTML export** — `Output/HtmlExporter.cs` with `HtmlTheme` (Light/Dark),
  `HtmlExportOptions` (theme, collapsibility, inline-CSS, title). `DebateResult`
  gains `ToHtml(HtmlExportOptions?)` and `SaveToHtmlAsync(file, options?, ct)`.
  Self-contained HTML with inline CSS, collapsible `<details>` rounds,
  knowledge/operator sections, token statistics, printable layout.
- **F-10b `WithTimeout(TimeSpan)`** — debate-level wall-clock timeout that links
  a `CancellationTokenSource` with the caller's CT via
  `CreateLinkedTokenSource`. `DebateTimeout` exposed on executor.
- **F-10c `Persona` presets** — 6 built-in system-prompt fragments:
  `Expert`, `DevilsAdvocate`, `CautiousOptimist`, `DataDrivenAnalyst`,
  `RiskManager`, `Pragmatist`. `Persona.All` dictionary + `Persona.Resolve(name)`.
- **F-10d `CouncilBenchmark`** — `Benchmarking/CouncilBenchmark.cs` with
  `AddConfiguration(name, configure)` + `WithQuestion` + `WithMaxRounds`.
  `RunAsync` runs configs sequentially (failures recorded per-entry).
  `BenchmarkReport.ToMarkdown()` + `SaveComparisonAsync()` render side-by-side
  verdicts / token usage / latency tables.
- **F-10e `WithParticipantLimit(int)`** — guards against misconfiguration in
  dynamic DI-driven setups; throws `InvalidOperationException` on `Build()` if
  exceeded.
- 27 unit tests.

### Added — F-07 Debate Templates & Presets Library

- **`Delibera.Core/Templates/DebateTemplate.cs`** — abstract `DebateTemplateBase`
  fluent facade over `CouncilBuilder`. `DebateTemplate` static accessor with 6
  built-in templates: `ArchitectureReview`, `RiskAssessment`, `CodeReview`,
  `ProductDecision`, `SecurityAudit`, `DataArchitecture`. `DebateTemplate.Custom()`
  escape hatch to a fresh `CouncilBuilder`. `ConfigureCore` runs eagerly inside
  `WithProvider` so later fluent overrides take precedence over template defaults.
- Fluent API mirrors `ICouncilBuilder`: `WithQuestion` / `WithProvider` /
  `WithMaxRounds` / `WithTemperature` / `WithResponseLanguage` / `WithSystemPrompt` /
  `SaveResultTo` / `WithTelemetry` / `WithTimeout` / `WithParticipantLimit` /
  `WithKnowledgeKeeper` / `AddMember` / `WithChairman` + `Advanced` escape hatch.
- 21 unit tests.

### Added — F-01 Async Streaming Council

- **`ICouncilExecutor.StreamDebateAsync(CancellationToken)`** added as DIM
  with default implementation that calls `ExecuteAsync` and yields rounds at
  the end (back-compat for external implementations).
- **`CouncilExecutor.StreamDebateAsync`** override streams rounds **live**: the
  debate runs on a background `Task`; the strategy's existing
  `onRoundCompleted` callback bridges each completed round into an unbounded
  `Channel<DebateRound>` (natural backpressure — strategy won't produce round
  N+1 until the callback for N returns); the iterator reads from the channel
  and yields each round as it completes.
- `DebateRound.Total` (int?) and `IsFinal` (bool) properties added. `Total`
  stamped by `StreamDebateAsync` on every yielded round
  (`maxRounds + 1` when Chairman set, else `maxRounds`). `IsFinal` true when
  the round name contains "Verdict" or "Final", or `RoundNumber >= Total`.
- `ICouncilExecutor.LastStreamedResult` exposes the aggregated `DebateResult`
  after the stream completes (with logs/token stats).
- `OnRoundCompleted` event still fires for each round (back-compat).
- `ExecuteAsync` stays fully backward-compatible.
- 13 unit tests.

### Added — F-09 Dynamic Strategy Switching

- **`Delibera.Core/Debate/IStrategySelector.cs`** — `IStrategySelector` interface
  (`SelectNextAsync` returns null or new strategy). `DebateProgress` record
  (`CurrentRound`, `MaxRounds`, `CompletedRounds`, `ResponseDiversityScore`,
  `IsStalemate`). `AdaptiveStrategySelector` built-in implementation:
  - `Initial` + `OnStalemate` strategies (`required`).
  - `StagnationThreshold` (default 2) consecutive low-diversity rounds.
  - `StagnationScore` cutoff (default 0.3).
  - Switches once per debate, then resets via `Reset()`.
  - Falls back to Levenshtein text-similarity when no embedding provider
    is configured (diversity score = 0.0 sentinel).
  - No switch on last round.
- **`DebateRound.StrategyUsed`** (`IDebateStrategy?`) — audit trail stamped on
  every round when a selector is configured.
- **`CouncilBuilder.WithAdaptiveStrategy(IStrategySelector)`** + `ICouncilBuilder`
  surface — sets `Initial` strategy as starting strategy when
  `AdaptiveStrategySelector` is used.
- `CouncilExecutor` instruments the round callback to compute response
  diversity (Levenshtein-based fallback), invoke `SelectNextAsync` after each
  round, log strategy switch, stamp `StrategyUsed` on every round.
- 16 unit tests.

### Added — F-02 Pluggable Vote / Consensus Engine

- **`Delibera.Core/Voting/IVotingStrategy.cs`** — `IVotingStrategy` interface
  (`MethodName` + `TallyAsync`). `RankedOption`, `ParticipantBallot`,
  `VotingResult` records. 3 built-in strategies:
  - **`MajorityVotingStrategy`** — top-ranked option gets 1 point each.
  - **`BordaCountVotingStrategy`** — N-1 points for top, N-2 for second, etc.
  - **`WeightedVotingStrategy`** — per-member `MemberWeights` overrides.
- **`Chairman.CreateVoting(model, provider, strategy)`** factory encodes the
  strategy in the Chairman's `PersonaPrompt` via `VotingChairmanMarker` so
  `CouncilExecutor` can detect it at runtime.
- **`CouncilBuilder.WithVotingChairman(model, provider, strategy)`** +
  `ICouncilBuilder.WithVotingChairman`.
- `DebateResult.VotingTally` (`VotingResult?`) property. `ToMarkdown()` renders
  a `🗳️ Voting Tally` section with method, winning option, and full score table.
- `CouncilExecutor.RunVotingAsync` extracts numbered/bulleted options from the
  final round's responses, asks each member to rank them, parses `'1,2,3'`
  replies, builds `ParticipantBallot`s, tallies via `IVotingStrategy.TallyAsync`.
- `WeightedVotingStrategy` validates non-negative weights and at least one
  positive weight.
- 16 unit tests.

### Added — F-05 Structured Output / JSON Schema

- **`Delibera.Core/Output/IStructuredOutputSerializer.cs`** — `IStructuredOutputSerializer`
  interface (`GenerateSchema<T>` + `Deserialize<T>`). `JsonSchemaOutputSerializer`
  default implementation:
  - Uses .NET 10 `JsonSchemaExporter.GetJsonSchemaAsNode` for schema generation.
  - `ExtractJson` helper strips markdown code fences and surrounding prose.
  - `BuildStructuredPrompt` appends schema + type name to the synthesis prompt.
  - `BuildCorrectionPrompt` builds the retry prompt on deserialisation failure.
  - Default `TypeInfoResolver` set for .NET 10 compatibility.
- **`ICouncilExecutor.ExecuteTypedAsync<TVerdict>(CancellationToken)`** added as
  DIM with default implementation that uses `DebateResult.GetTypedVerdict<T>`.
- `CouncilExecutor` overrides `ExecuteTypedAsync<T>`: runs `ExecuteAsync`, attempts
  to deserialise existing `FinalVerdict`, on failure re-prompts Chairman with
  correction prompt + schema, stamps `TypedVerdict` on `DebateResult` on success.
  One automatic retry with failure logging.
- `DebateResult.TypedVerdict` (`object?`) + `GetTypedVerdict<TVerdict>()` helper.
- `CouncilBuilder.WithStructuredOutput<TVerdict>(serializer?)` +
  `ICouncilBuilder.WithStructuredOutput<TVerdict>`.
- 21 unit tests.

### Added — F-03 Debate Persistence & Resume

- **`Delibera.Core/Persistence/`** — new namespace with:
  - **`IDebateStore`** interface (`Save` / `Load` / `List` / `Delete`).
  - **`DebateCheckpoint`** record (`DebateId`, `CreatedAt`,
    `LastCompletedRound`, `CompletedRounds`, `Options` snapshot,
    `OriginalQuestion`).
  - **`DebateCheckpointMeta`** lightweight metadata record.
  - **`GenerateId()`** — ULID-style 26-char lexicographically-sortable ID
    (timestamp prefix + random suffix).
  - **`CreateEmpty()`** — factory for fresh checkpoints.
  - **`FileDebateStore`** — atomic JSON write-temp → rename; optional
    `RetentionDays`; thread-safe with `SemaphoreSlim`; lazy retention sweep on
    `ListAsync`.
  - **`InMemoryDebateStore`** — `ConcurrentDictionary`-backed, for testing.
- **`CouncilOptions.Persistence`** sub-section with `PersistenceOptions`:
  `Enabled`, `Store` (File/InMemory), `Directory`, `RetentionDays`,
  `ResumeFromDebateId`.
- `CouncilBuilder.WithPersistence(IDebateStore)` + `ResumeFrom(debateId)` +
  `ICouncilBuilder`.
- `CouncilExecutor.SaveCheckpointAsync`: after each round, builds a checkpoint
  with completed rounds, options snapshot, and reuses existing debate id
  (from `ResumeFrom` or a same-question match). Errors during save are reported
  via `ReportError` (don't abort debate).
- 24 unit tests.

### Added — F-04 Agent Memory & Long-Term Context

- **`Delibera.Core/Memory/IAgentMemory.cs`** — `IAgentMemory` interface
  (`Store` / `Recall` / `Delete`). `MemoryEntry` record (`Content`, `CreatedAt`,
  `Metadata`). 3 implementations:
  - **`InMemoryAgentMemory`** — `ConcurrentDictionary`-backed, Jaccard
    token-overlap similarity (no embedding provider required).
  - **`QdrantAgentMemory`** — per-agent collection, uses `IRagProvider`
    `SearchAsync` + `IEmbeddingProvider` for semantic similarity.
  - **`PgVectorAgentMemory`** — shared table filtered by `agent_name` metadata.
- `CouncilBuilder.WithAgentMemory(IAgentMemory?)` + `ICouncilBuilder` — null
  parameter defaults to `InMemoryAgentMemory` (no persistence).
- `CouncilExecutor`:
  - **Pre-execution**: recalls each member's top-3 memories, dedupes by content,
    prepends a `Memory from previous sessions` block to the system prompt for
    all participants.
  - **Post-execution**: stores each member's last response and the Chairman's
    final verdict as `MemoryEntry` records, tagged with member display name +
    debate id (when persistence enabled).
  - Both phases log to `ExecutionLog` under source `AgentMemory`. Errors during
    recall/store are reported but do not abort the debate.
- 13 unit tests.

### Added — ConsoleApp demos

9 new `--flag` demo entries:
- `--telemetry` (**TelemetryExample**) — in-process `ActivityListener` +
  `MeterListener` printing every span and metric.
- `--quick-wins` (**QuickWinsExample**) — HTML export, timeout, personas,
  benchmark, participant limit.
- `--templates` (**TemplatesExample**) — `ArchitectureReview` template
  against a live Ollama endpoint.
- `--stream` (**StreamingCouncilExample**) — `IAsyncEnumerable<DebateRound>`
  live output with round-by-round progress.
- `--adaptive-strategy` (**AdaptiveStrategyExample**) — `StandardDebate` →
  `CritiqueDebate` switch on stagnation.
- `--voting` (**VotingExample**) — `WeightedVotingStrategy` with per-member
  weights.
- `--structured-output` (**StructuredOutputExample**) — `ArchitectureDecision`
  typed verdict.
- `--persistence` (**PersistenceExample**) — `FileDebateStore` with retention
  + auto-resume on existing checkpoint.
- `--agent-memory` (**AgentMemoryExample**) — `InMemoryAgentMemory` across
  successive debates.

### Changed

- Bumped `Delibera.Core` package version `10.2.5` → `10.2.6`.
- `CouncilMember.PersonaPrompt` property now assigned in the constructor
  (was previously null — fixed for F-02 voting chairman detection).
- New `DebateRound.Total` and `IsFinal` properties; `StrategyUsed` property
  (nullable).
- New `DebateResult.TypedVerdict` and `VotingTally` properties; Markdown
  output gains `🗳️ Voting Tally` section.
- `<Description>` and `<PackageTags>` updated to mention new features.

### Compatibility

- **No breaking changes.** Every new feature is opt-in via additional builder
  methods (`WithTelemetry`, `WithTimeout`, `WithVotingChairman`,
  `WithStructuredOutput<T>`, `WithPersistence`, `WithAgentMemory`,
  `WithAdaptiveStrategy`). The fluent API is fully backward compatible.

### Test summary

- 9 new test files, 163 new tests, **all 259 tests pass** (up from 105 in
  10.2.5).

## [10.2.4] - 2026

This release delivers **full cooperative `CancellationToken` support across every
public async method** — a single cancel signal now aborts the entire pipeline
(rounds, Chairman synthesis, LLM calls, MCP tool invocations, RAG queries and
file writes) via `OperationCanceledException`. It also adds **in-memory
`MarkdownKnowledgeBase` loaders** that ingest markdown bodies without temp
files, with optional per-source metadata.

This release delivers **full cooperative `CancellationToken` support across every
public async method** — a single cancel signal now aborts the entire pipeline
(rounds, Chairman synthesis, LLM calls, MCP tool invocations, RAG queries and
file writes) via `OperationCanceledException`. It also adds **in-memory
`MarkdownKnowledgeBase` loaders** that ingest markdown bodies without temp
files, with optional per-source metadata.

### Added — In-memory `MarkdownKnowledgeBase` loaders

- **`KnowledgeDocument`** — new sealed record in `Delibera.Core.Knowledge`
  (`string Name`, `string Content`, `IReadOnlyDictionary<string, string>? Metadata`)
  used to tag documents with per-source metadata so the council can distinguish
  "contract" from "discovery context" inside the KB.

- **`MarkdownKnowledgeBase.LoadTextAsync(string content, string sourceName, CancellationToken)`**
  — ingest a markdown body without writing a temp file. The `sourceName`
  appears in the council's per-round context, just like a file path would.

- **`MarkdownKnowledgeBase.LoadTextAsync(KnowledgeDocument, CancellationToken)`**
  — overload that preserves the supplied `Metadata` on the indexed document.

- **`MarkdownKnowledgeBase.LoadTextsAsync(IEnumerable<KnowledgeDocument>, CancellationToken)`**
  — sequential bulk ingest with cooperative cancellation checked between
  documents.

- **`MarkdownKnowledgeBase.DocumentMetadata`** — read-only snapshot
  (`IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>?>`) of the
  per-source metadata captured at load time. Sources loaded through the
  file-path API appear as `null` entries.

### Added — Full cooperative `CancellationToken` support

Every public async method in the library now honors a `CancellationToken`
cooperatively. A single cancel signal aborts the entire pipeline — rounds,
Chairman synthesis, LLM calls, MCP tool invocations, RAG queries and even
file writes — via `OperationCanceledException`.

#### Changed — Additive CT parameters (no breaking binary changes)

- **`IKnowledgeBase.LoadAsync(string, CancellationToken)`** and
  **`IKnowledgeBase.LoadManyAsync(IEnumerable<string>, CancellationToken)`** —
  `CancellationToken ct = default` added to existing method signatures.
  **Source-breaking** for any external implementer of the interface;
  **binary-compatible** for callers (parameter has a default value).

- **`MarkdownKnowledgeBase`** — the file-path API
  (`LoadAsync`, `LoadManyAsync`, `LoadDirectoryAsync`) now accepts a
  `CancellationToken` and forwards it to `File.ReadAllTextAsync` and between
  files in bulk operations.

- **`DebateResult`** — all save methods
  (`SaveToMarkdownAsync`, `SaveStatisticsAsync`, `SaveLogsAsync`, `SaveAllAsync`,
  `SaveToFileAsync`) accept a `CancellationToken` and forward it to
  `File.WriteAllTextAsync`. `SaveAllAsync` propagates the token to each
  individual save call.

- **`CouncilExecutor.ExecuteAsync`** — the caller's `CancellationToken` is now
  forwarded to the final `result.SaveToFileAsync(_outputPath, ct)` step,
  closing the top-level cancellation chain end-to-end.

#### Added — Hosted-service helper

- **`Delibera.Core.Extensions.IAppStoppingToken`** — minimal abstraction
  (`CancellationToken ApplicationStopping { get; }`) that any host lifetime
  can implement in three lines without forcing a `Microsoft.Extensions.Hosting`
  dependency on `Delibera.Core`.

- **`Delibera.Core.Extensions.CouncilExecutorLifetimeExtensions.ExecuteAsync(
  ICouncilExecutor, IAppStoppingToken, CancellationToken)`** — links the
  caller's `CancellationToken` with `IAppStoppingToken.ApplicationStopping`
  via `CancellationTokenSource.CreateLinkedTokenSource` so a host shutdown
  (ASP.NET Core, Worker Service, etc.) cancels the debate cooperatively.
  Whichever signal fires first wins.

#### Added — ConsoleApp demo

- **`Delibera.ConsoleApp.Examples.CancellationExample`** — run with
  `--cancellation`. Wires `Console.CancelKeyPress` to a
  `CancellationTokenSource` and forwards the token through
  `executor.ExecuteAsync(cts.Token)`. Includes a heartbeat task that proves
  the main thread is alive while the debate is awaiting.

- The default `Program.Main` now also wires Ctrl+C for the main demo path
  so a user can cancel any run with a single keystroke. Example-mode
  dispatchers (e.g. `--cancellation`) keep ownership of their own
  `CancelKeyPress` handlers and are not double-bound.

### Tests

- **9 new tests** in `MarkdownKnowledgeBaseTests` for the in-memory loaders:
  string overload happy-path, `KnowledgeDocument` overload happy-path with
  metadata assertion, `LoadTextsAsync` bulk happy-path, source-name
  overwrite semantics, and **6 cancellation tests** covering pre-canceled
  tokens on every public method plus `LoadManyAsync` / `LoadDirectoryAsync`
  honoring cancellation between files.
- **7 new tests** in `DebateResultCancellationTests` covering all save
  methods with pre-canceled tokens + a happy-path test for `SaveAllAsync`
  that verifies the three files are produced.
- **2 new tests** in `CouncilExecutorCancellationTests` covering the
  executor's CT-aware save path and a positive no-cancel run.
- **5 new tests** in `CouncilExecutorLifetimeExtensionsTests` covering the
  `IAppStoppingToken` helper: caller-token respected, application-stopping
  token respected, run-to-completion when neither is canceled, and
  null-argument validation on both `executor` and `lifetime`.

### Documentation

- New **Cancellation Support** section in `Delibera.Core/README.md` with 4
  worked examples (timeout, manual cancel, ASP.NET Core
  `IHostApplicationLifetime` adapter, Console Ctrl+C) and a table of
  cancellable operations.
- New **Cancellation Support** section in the repo-root `README.md` with a
  quick-start example and a link to the full guide.
- Feature row added to the Key Features table in both READMEs.
- `MarkdownKnowledgeBase.LoadTextAsync` and `LoadTextsAsync` added to the
  cancellable-operations table in `Delibera.Core/README.md`.

### Compatibility

- **No breaking binary changes.** All new `CancellationToken` parameters
  have a default value, so existing compiled callers continue to link and
  run unchanged. External implementers of `IKnowledgeBase` must add the
  `CancellationToken` parameter to their method signatures — this is a
  source-level break for that scenario only.
- **No behavior change** for callers that don't pass a token: the
  pre-v10.2.4 behavior is preserved exactly.

## [10.2.3] - 2026

### Added — AutoChunking (progressive disclosure for large documents)

- **AutoChunking** — automatic splitting of large knowledge documents (contracts, reports, articles)
  into context-window-sized chunks distributed across debate rounds via progressive disclosure.
  When a document exceeds the smallest model's context window, the orchestrator creates a
  `ChunkingPlan` and distributes chunks evenly across rounds so every model receives a complete
  view of the document by the final round.

- **`Chunking` namespace** (`Delibera.Core.Chunking`):
  - `AutoChunker` — static planner with 3 strategies: `SemanticBoundary` (respects Markdown
    headers → paragraphs → sentences), `FixedSize`, `SlidingWindow` (50% overlap).
  - `AutoChunkingOrchestrator` — analyses model capabilities, calculates overhead, creates
    the plan, and distributes chunks per round.
  - `AutoChunkingOptions` — configuration: strategy, safety margin, max chunks/round,
    Map-Reduce toggle, progressive disclosure toggle.
  - `ChunkingPlan` / `DocumentChunk` — immutable records describing the split.

- **`ModelCapabilities`** record — context window, max output tokens, vision/tool support,
  model family. Obtained from providers via the new `ILLMProvider.GetModelCapabilitiesAsync()`.

- **`ModelContextWindowRegistry`** — static registry of 40+ popular models (Llama, Qwen,
  DeepSeek, Phi, Mistral, Gemma, GPT, Claude, YandexGPT, …) with their context window sizes.
  `Register()` for custom models. Case-insensitive substring matching.

- **`ILLMProvider.GetModelCapabilitiesAsync(string model, CancellationToken)`** — new
  default interface method (returns `null`). Overridden by:
  - `OllamaProvider` — queries `/api/show` and extracts `num_ctx` from Modelfile parameters
    via regex. Falls back to `ModelContextWindowRegistry`.
  - `ChatClientLLMProvider` / `YandexGptProvider` — fall back to `ModelContextWindowRegistry`.

- **`PromptContext` extended** — new fields: `ChunkingPlan`, `AutoChunkingEnabled`,
  `MinContextWindow`. New method `GetChunkedUserPrompt(roundNumber, totalRounds, previousRounds)`
  returns round-appropriate chunks with `[Chunk X/Y]` markers and section titles.

- **`DebateScenario.BuildChunkedPrompt()`** — protected helper used by all built-in strategies
  (`StandardDebate`, `CritiqueDebate`, `ConsensusDebate`). Replaces `GetFullUserPrompt()` calls
  with round-aware chunk distribution.

- **`CouncilBuilder` bulk configuration**:
  - `WithOptions(CouncilOptions options)` — apply a pre-built options snapshot.
  - `WithOptions(Action<CouncilOptions> configure)` — inline lambda configuration.
  - `CouncilBuilder(CouncilOptions options)` constructor — one-shot setup.
  - `WithAutoChunking(AutoChunkingOptions?)` — enable chunking via fluent API.
  - `WithModelContextWindow(pattern, tokens)` — register custom model context windows.
  - `ApplyOptions()` — transfers all non-default `CouncilOptions` fields to the builder.

- **`ICouncilBuilder` updated** — new methods: `WithOptions(CouncilOptions)`,
  `WithOptions(Action<CouncilOptions>)`, `WithAutoChunking(...)`, `WithModelContextWindow(...)`.

- **`AutoChunkingConfig`** in `CouncilOptions` — bound from `Delibera:AutoChunking`
  configuration section. Fields: `Enabled`, `Strategy`, `SafetyMargin`, `MaxChunksPerRound`,
  `EnableMapReduce`, `EnableProgressiveDisclosure`, `ModelContextWindows` (dictionary).
  `ToOptions()` converts to `AutoChunkingOptions` and registers custom model windows.

- **DI auto-wiring** — `AddDelibera(IConfiguration, ILoggerFactory, ...)` now resolves
  `IOptions<CouncilOptions>` and passes it to `new CouncilBuilder(options)`, so all
  settings (strategy, rounds, temperature, compression, auto-chunking, etc.) are applied
  automatically. Explicit builder calls take precedence.

- **Console example** `AutoChunkingExample` (run with `--autochunking`) demonstrating:
  - 3 configuration paths: fluent API, options snapshot, lambda.
  - Offline chunking plan demo for 4K/8K/32K/128K context windows.
  - Model context window registry dump.
  - Synthetic contract document (~15K+ chars) that triggers chunking on small-context models.

### Changed

- Bumped `Delibera.Core` package version `10.2.2` → `10.2.3`.
- `CouncilExecutor` constructor now accepts optional `AutoChunkingOptions?` parameter.
- `CouncilExecutor.ExecuteAsync()` invokes `AutoChunkingOrchestrator.PrepareContextAsync()`
  before the debate when AutoChunking is enabled.
- `CouncilExecutor.GetInfo()` displays AutoChunking configuration when active.
- `appsettings.json` updated with `AutoChunking` section.

### Compatibility

- **No breaking changes.** AutoChunking is opt-in — disabled by default. All existing
  `ILLMProvider` implementations continue to work (default `GetModelCapabilitiesAsync`
  returns `null`). `PromptContext` is a `record` with `with`-expression support, so
  existing code that constructs it directly is unaffected. The new `CouncilExecutor`
  constructor parameter is optional.

### Added — Polly v8 resilience via Microsoft.Extensions.Http.Resilience

- **Microsoft.Extensions.Http.Resilience 10.7.0** dependency (transitively brings Polly v8
  `Polly.Core` + `Microsoft.Extensions.Http`). Hand-rolled retry loops in `OllamaProvider`,
  `YandexGptProvider`, and `McpClientAdapter` have been **removed** in favour of named
  Polly v8 pipelines registered through DI.

- **`ResilienceOptions`** (bound from `Delibera:Resilience` configuration section)
  configures: `MaxRetryAttempts`, `BaseDelay`, `MaxDelay`, `UseJitter`, `BackoffType`
  (`"Exponential"` / `"Linear"` / `"Constant"`), `RetryableStatusCodes`, `AttemptTimeout`,
  master `Enabled` flag. All values are live-tracked through `IOptionsMonitor<>` so option
  changes are honoured without rebuilding the container.

- **`IDeliberaResiliencePipelineProvider`** — central registry of named Polly v8
  pipelines with three built-in keys:
  - `Delibera.Local` — retries connection-level failures only (no status code); used by
    `OllamaConnectionMode.Local`.
  - `Delibera.Cloud` — retries transient HTTP responses (configurable allow-list,
    default `[408, 429, 500, 502, 503, 504, 524]`) plus `HttpRequestException` /
    `TaskCanceledException`; used by `OllamaConnectionMode.Cloud`, `YandexGptProvider`,
    and `McpClientAdapter`'s HTTP transport.
  - `Delibera.Default` — alias for the more permissive of the two; used when a consumer
    does not specify a pipeline key.

- **`AddDeliberaResilience(IServiceCollection, Action<ResilienceOptions>?)`** —
  one-call DI setup. Registers `IDeliberaResiliencePipelineProvider`, plus three named
  `HttpClient` entries (`Delibera.Ollama.Local`, `Delibera.Ollama.Cloud`,
  `Delibera.YandexGPT`) each wired with `AddResilienceHandler` so retries apply to the
  HttpClient handler chain (the standard `Microsoft.Extensions.Http.Resilience` pattern).

- **`AddDeliberaResiliencePipeline(name, build)`** — register custom Polly v8
  pipelines under arbitrary keys. The pipeline registry merges them into its lookup
  table alongside the built-ins.

- **`AddDeliberaHttpClient(name, pipelineName, configure)`** — register an arbitrary
  named HttpClient whose handler pipeline is decorated with the chosen
  Polly v8 pipeline. Useful for additional HTTP integrations beyond Delibera's
  built-in providers.

- **`OllamaProvider` DI path** — `OllamaProvider.ForLocal(endpoint, IHttpClientFactory,
  IDeliberaResiliencePipelineProvider, ...)` and `ForCloud(...)` overloads construct
  the provider from the factory's named HttpClient and the operation-level pipeline
  (`GetOperationPipeline`). The hand-rolled `for` loop and `IsTransientHttp` helper
  have been deleted; transient failures are now retried by the configured pipeline.

- **`YandexGptProvider` DI path** — new constructor accepts `IHttpClientFactory?` +
  `IDeliberaResiliencePipelineProvider?`. The original `(apiKey, folderId, ...)`
  constructor remains for backward compatibility — providers constructed without DI
  still work, just without retry.

- **`McpClientAdapter` DI path** — new constructor accepts `IHttpClientFactory?` +
  logical client name. When wired, the adapter injects the factory-managed HttpClient
  into the ModelContextProtocol `HttpClientTransport` so retries apply to MCP HTTP/SSE
  traffic.

- **`ResilientHttpClientExtensions.SendAsync(http, pipeline, request, ...)`** —
  extension helper that runs an `HttpClient` request through a typed
  `ResiliencePipeline<HttpResponseMessage>`. Handles request cloning between attempts
  (HttpClient disposes the request after the first attempt).

- **Console example** `ResilienceExample` (run with `--resilience`) showing how to
  wire up `AddDeliberaResilience`, register a custom pipeline, and read the live
  configuration through `IOptionsMonitor`.

- **Tests** — 9 new unit tests in `ResilienceTests` covering `ResilienceOptions`
  defaults, the pipeline registry, `AddDeliberaResilience` DI wiring, configuration
  binding from `Delibera:Resilience`, and custom-pipeline registration.

### Changed

- Bumped `Delibera.Core` package version `10.2.0` → `10.3.0`.
- Bumped console app dependencies to `Microsoft.Extensions.Http` 10.0.9 +
  `Microsoft.Extensions.Http.Resilience` 10.7.0.
- `OllamaProvider.Client` is now constructed eagerly inside the provider's
  constructor (was lazy before). `OllamaEmbeddingProvider`'s constructor still reads
  `ollamaProvider.Client` synchronously, so the eager construction preserves the
  existing pattern.
- `ResilienceOptions` is bound from the `Delibera:Resilience` configuration section
  inside `AddDelibera(IConfiguration, ...)` so `IOptionsMonitor<ResilienceOptions>`
  flows through DI transparently.

### Compatibility

- **No breaking changes for non-DI consumers.** The legacy `OllamaProvider(endpoint,
  apiKey, ...)`, `YandexGptProvider(apiKey, folderId, ...)`, and `McpClientAdapter(config)`
  constructors remain in place and behave exactly as before — without retries (the
  behaviour before v10.2.2). Consumers that want retry semantics opt in by using the
  new DI-aware overloads.
- **No breaking changes for DI consumers either** unless they previously relied on
  the hand-rolled retry semantics in `OllamaProvider.ChatAsync` (not exposed
  publicly; the loop was internal).

## [10.2.0] - 2026

### Added — Microsoft.Extensions.Logging, response-language enforcement, parallel Operator

- **Microsoft.Extensions.Logging support** — inject your own `ILogger` / `ILoggerFactory` and every
  debate event (Chairman opening, Knowledge Keeper queries, compression, Operator interactions,
  participant responses, errors) is forwarded to the host's logging pipeline. New APIs:
  - `ICouncilBuilder.WithLogger(ILogger?)` and `CouncilBuilder.WithLogger(...)`.
  - `ICouncilExecutor.Logger` property exposing the configured `ILogger`.
  - `AddDelibera(IServiceCollection, IConfiguration, ILoggerFactory, string?)` DI overload that
    auto-decorates every resolved `ICouncilBuilder` with a logger.
  - `DebateExecutionOptions` record bundles the logger (plus response language + parallelism)
    threaded through `IDebateStrategy.ExecuteAsync(...)` via a new
    `IDebateStrategyWithOptions` interface (default method on `IDebateStrategy` keeps custom
    strategies working unchanged).
- **Response-language enforcement** — `ICouncilBuilder.WithResponseLanguage(string?)` and
  `CouncilOptions.ResponseLanguage`. When set, Delibera injects a strict directive into every
  system and user prompt so all models (participants, Chairman, Knowledge Keeper, Operator) answer
  exclusively in the chosen language, regardless of the prompt or retrieved RAG context.
- **Parallel Operator requests** — `[[OPERATOR: …]]` tasks delegated by participants within a round
  now run concurrently via `Parallel.ForEachAsync`, bounded by
  `ICouncilBuilder.WithMaxDegreeOfParallelism(int)` / `CouncilOptions.MaxDegreeOfParallelism`
  (0 = unbounded, default). Delibera-shipped strategies (`StandardDebate`, `CritiqueDebate`,
  `ConsensusDebate`) opt in via the new `ExecuteAsync(..., DebateExecutionOptions, ...)` overload.

### Changed

- **Renamed `LogLevel` enum to `ExecutionLogLevel`** (in `Delibera.Core.Models`) to avoid a name
  clash with `Microsoft.Extensions.Logging.LogLevel`, which is now referenced throughout the
  framework. The `ExecutionLog.Level` field, `DebateResult.ToLogsMarkdown()`, and the console
  demo all use the renamed enum. `ExecutionLog.ToMicrosoftLogLevel()` maps to the M.E.Logging
  severity. This is a source-breaking change for consumers that referenced `LogLevel.Info` etc.
  directly; replace with `ExecutionLogLevel.Info`.

## [10.1.1] - 2026

### Added — Microsoft.Extensions.AI integration

- **`Microsoft.Extensions.AI` 10.7.0** dependency in `Delibera.Core`.
- **`ChatClientLLMProvider`** — adapts any Microsoft.Extensions.AI `IChatClient` to Delibera's
  `ILLMProvider`. Works with OpenAI, Azure OpenAI, Ollama, Anthropic and local OpenAI-compatible
  servers (LM Studio, LocalAI, vLLM) without a bespoke provider per vendor.
- **`EmbeddingGeneratorProvider`** — adapts any `IEmbeddingGenerator<string, Embedding<float>>`
  to Delibera's `IEmbeddingProvider` for RAG indexing/querying.
- **`ILLMProvider.ChatStreamAsync(...)`** — additive default interface method for token-by-token
  streaming. `ChatClientLLMProvider` overrides it for true streaming; existing providers fall back
  to a single `ChatAsync` call, so nothing breaks.
- **`MicrosoftAIExtensions`** bridge helpers:
  - `IChatClient.AsLLMProvider(...)`, `IEmbeddingGenerator.AsEmbeddingProvider(...)`
  - `ILLMProvider.AsChatClient(...)` (reverse bridge so Delibera providers can join a
    Microsoft.Extensions.AI middleware pipeline)
  - `IChatClient.WithMiddleware(...)` to compose function invocation + logging.
- **`ProviderFactory.CreateFromChatClient(...)`** — build a cached provider directly from an `IChatClient`.
- **`OllamaProvider.AsChatClient()` / `AsEmbeddingGenerator()`** — expose the underlying
  OllamaSharp client (which natively implements the Microsoft.Extensions.AI interfaces).
- **DI helpers** `AddDeliberaChatClient(...)` and `AddDeliberaEmbeddingGenerator(...)` in
  `ServiceCollectionExtensions`.
- **Console example** `MicrosoftExtensionsAiExample` (run with `--msai`).
- **Unit test project** `tests/Delibera.Core.Tests` (xUnit) covering the new providers, the bridge
  adapters and the factory — plus a solution file `Delibera.slnx`.

### Changed

- Bumped `Delibera.Core` package version `10.1.0` → `10.1.1`.
- Documentation: `README.md` / `README-RU.md` and `docs/QuickStart*.md` updated with a
  Microsoft.Extensions.AI section.

### Compatibility

- **No breaking changes.** The public API is fully backward compatible; all existing
  `ILLMProvider` / `IEmbeddingProvider` consumers continue to work unchanged.

## [10.1.0] - 2026

- Operator (MCP tools) role, .NET 10 / C# 15 (preview) upgrade, high-performance hot-path
  optimizations (SIMD cosine similarity, allocation-free token counting, pooled cache hashing).
- Dependency injection, context compression, and RAG (Qdrant, pgvector) support.
