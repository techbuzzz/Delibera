# W4 — Server, Redis, Observability

> **Goal:** the HTTP/MCP surface tells the truth about what the service does, and nothing
> runs that the operator cannot see.
> **Verification note:** findings marked *(unverified)* come from static reading only —
> the container/HTTP behaviour has not been executed yet. Confirm before changing code.
> **Status:** 7 / 10 done (W4-01, W4-02, W4-03, W4-05, W4-06, W4-07, W4-09)

---

## W4-01 · `WithOpenApi` is deprecated in .NET 10 · **P1** · ✅ done

**Problem.** Build reports `ASPDEPR002` in four files:
`DebateEndpoints.cs:14`, `ScenarioEndpoints.cs:15`, `TemplateEndpoints.cs:10`,
`CorpusEndpoints.cs:10`:

> `WithOpenApi` is obsolete: will be removed in a future release.

The generated OpenAPI document is part of the public contract of a library whose selling
point is "point your client at the endpoint". It must be migrated before removal, not
after.

**Fix (done, and smaller than expected).** Checked the official guidance rather than
guessing — the .NET 10 breaking-change page
(https://learn.microsoft.com/aspnet/core/breaking-changes/10/withopenapi-deprecated) says to
**remove the `.WithOpenApi()` calls**: the method duplicated behaviour the built-in document
pipeline already provides, and `AddOpenApiOperationTransformer` is only needed for per-endpoint
tweaks *inside* the old callback. Delibera used the parameterless form, so there is nothing to
port. `Program.cs` already calls `AddOpenApi()` and maps `MapOpenApi()` in Development, so the
document is still produced; the `.WithTags()`, `.WithName()`, `.WithSummary()` and
`.Produces*()` metadata feeds it unchanged.

**Acceptance.**
- [x] Zero ASPDEPR002 warnings (verified on a `--no-incremental` build: 0 warnings overall)
- [x] The four call sites replaced by a one-line comment explaining why there is no call
- [ ] Smoke check of the generated `openapi.json` against a running server (needs the app to
      boot; not executed in this pass)
- [ ] `Microsoft.OpenApi` 2.7.5 pin revisited once the source-generator conflict is resolved
      (see the explanatory comment in `Delibera.Server.csproj:14-17`)

---

## W4-02 · Unhandled exceptions produce bare 500s · **P1** · ✅ done

**Problem.** The pipeline has neither `AddProblemDetails()` nor `UseExceptionHandler()`.
Exceptions thrown by endpoints (e.g. `DebateOrchestrationService.cs:186`) return an empty
500; in Development the auto-added developer exception page returns stack traces.
`DebateEndpoints.cs:24` advertises `ProducesProblem(503)` that the code never produces.
`GET /debates/{id}/result` returns 409 as `text/plain`, not `ProblemDetails`.

**Fix (done, verified against a running host).** `AddProblemDetails` + `UseExceptionHandler()`
+ `UseStatusCodePages()`, plus a `ProblemDetailsExceptionHandler`:

- The app is built with `CreateSlimBuilder`, which does **not** add the developer exception
  page — so unhandled exceptions produced a bare 500 with an unparseable empty body.
- The framework default only writes ProblemDetails outside Development, which would leave the
  behaviour environment-dependent. The handler writes it in **every** environment instead.
- `detail` carries the exception message **only in Development**; a stack trace never reaches
  the payload. The log entry gets the full exception, and the response body carries the
  correlation id so the caller can find it.
- The response that already started is left alone (it cannot be rewritten).

`Program.cs` now exposes `public partial class Program;` so the test project can start the
real pipeline — the integration tests below are the reason to believe this works, rather
than it merely compiling.

**Acceptance.**
- [x] The host starts with the new wiring (`Host_Starts_With_The_Serilog_And_ProblemDetails_Wiring`)
- [x] An unmatched route returns `application/problem+json`, not an empty 404
- [x] A forced exception returns 500 + ProblemDetails, and in Production the body contains
      neither the template id nor any internal type name
- [x] In Development the `detail` field is present, so the guarantee is explicit in our code
      rather than inherited from a framework default
- [x] The correlation id is in the body as well as the header, for the status-code path too
- [ ] `GET /debates/{id}/result` still returns 409 as `text/plain`; converting it to
      `ProblemDetails` and dropping the unproduced `ProducesProblem(503)` remain open

> **A regression this task introduced, and its own integration test caught.**
> The first version of the handler claimed *every* exception. That silently turned a missing
> `required` member — `BadHttpRequestException` raised while the request body is bound — into
> a **500**. Returning `false` for it did not help either: the bind happens downstream of the
> exception middleware, so the exception simply kept travelling and the server rendered the
> 500. The handler now claims that case explicitly and writes **400** with a
> "the request could not be read" title and a `LogWarning` instead of a `LogError`.
> `Missing_Required_Member_Is_A_400_Not_A_500` pins it. Reading `Program.cs` could not have
> shown this — only a real HTTP request through a running host did.

---

## W4-03 · Serilog is a dependency but never wired · **P1** · ✅ done

**Problem.** `CorrelationIdMiddleware.cs:19` pushes a Serilog `LogContext` property, and
`Serilog.AspNetCore 10.0.0` is referenced — but `UseSerilog(...)` appears nowhere in
`Program.cs`. The correlation id is therefore present in no log line, while the response
header suggests otherwise. The package is dead weight.

**Fix (done).** Wired: `builder.Host.UseSerilog(...)` reading the `Serilog` configuration
section when present, `ReadFrom.Services`, `Enrich.FromLogContext()` (this is what finally
activates the `LogContext` property the middleware pushes) and an explicit console sink so
the app logs even when the configuration section is absent. The old
`ClearProviders`/`AddConsole`/`AddDebug` block was removed — `UseSerilog` replaces the
logger factory, so keeping them would have been misleading.

**Acceptance.**
- [x] The host starts with the Serilog pipeline (covered by the integration test above)
- [x] `Enrich.FromLogContext()` is what makes the `X-Correlation-Id` property appear on
      every line the middleware scope covers
- [ ] Sinks beyond the console are a deployment choice; no secret is written by this change
      (no new logging call site was introduced)

---

## W4-04 · Custom metrics are silently dropped · **P1** *（unverified）* · ⬜ todo

**Problem.** `ServerServiceExtensions.cs:61-70`: traces have an `else AddConsoleExporter()`
fallback, metrics do not. With an empty `OtlpEndpoint` every custom Delibera metric
(round duration, token usage, cache hits, compression ratio) goes nowhere — and there is
no error, because that is a valid configuration.

**Fix.** Mirror the tracing fallback for metrics.

**Acceptance.**
- [ ] With no OTLP endpoint configured, metrics are exported to the console exporter
- [ ] Confirmed by starting the server and observing an export line *(unverified until run)*

---

## W4-05 · FluentValidation covers 1 of 4 request contracts · **P1** · ✅ done

**Problem.** A single validator exists (`CreateDebateRequest`). `ScenarioRequest`,
`CreateCorpusRequest` and `IndexDocumentRequest` have none, and `ValidationFilter.cs:19`
silently passes them through. Consequences: `POST /scenarios/validate` dereferences a null
on `{}` (NRE → 500), and `maxRounds` is unbounded — an anonymous caller can request an
arbitrary number of paid LLM rounds (see W1-04 on the absence of authentication).

**Fix (done).** `ScenarioRequestValidator` (plus a per-member validator), and
`CreateCorpusRequestValidator` / `IndexDocumentRequestValidator`. Auto-registration already
scans the assembly, so no wiring was needed.

Deliberate non-validations, both documented in the validators themselves:
- **`ScenarioRequest.Strategy` is not restricted.** The tested, documented behaviour for an
  unknown strategy is a graceful fallback to "Standard"; a rule here would silently turn that
  into a rejection. A test pins the fallback.
- **`CreateCorpusRequest.VectorStore` is not restricted** because `CorpusService` never reads
  it — validating it would enforce a contract the server does not implement.

`IndexDocumentRequest.Content` is the one that mattered: `CorpusService.EstimateChunks` split
it without a null check, so a missing body was a `NullReferenceException` and a 500.

**Acceptance.**
- [x] Empty question / empty member array / empty corpus name / empty content → 400 with a
      `ProblemDetails` body
- [x] `maxRounds = 5000` and `temperature = 7.5` → 400
- [x] Unknown strategy is still accepted by the validate endpoint
- [x] 9 integration tests in `RequestValidationTests`

---

## W4-06 · Documented 400, actual 422 · **P2** · ✅ done

**Problem.** `ValidationFilter.cs:25-27` returns **422 Unprocessable Entity** while every
endpoint declares `.ProducesValidationProblem()` (400). Generated clients will be built
against a status the service never returns.

**Fix (done).** The filter now returns **400**, which is what every endpoint declares via
`.ProducesValidationProblem()` and what ASP.NET Core itself uses. The comment records why, so
nobody "corrects" it back to 422.

**Acceptance.**
- [x] The filter's status and every `.ProducesValidationProblem()` agree
- [x] A validation failure responds with `application/problem+json` (RFC 7807), not
      `application/json`
- [ ] Changelog entry: clients written against the 422 behaviour need to know

---

## W4-07 · `CorpusService` singleton is not thread-safe · **P1** · ✅ done

**Problem.** `CorpusService` is registered as a singleton (`ServerServiceExtensions.cs:28`)
and mutates a plain `Dictionary` + shared `List` (`CorpusService.cs:15,23,43,64,67`).
`ListCorpora()` enumerates `_store.Values` while a request adds to it; the read-modify-write
at `:67` loses updates under concurrency.

**Fix (done).** The store is a `ConcurrentDictionary` and each corpus holds its documents
copy-on-write as an immutable `CorpusEntry` record. Both mutating operations are
**compare-and-swap** loops (`TryUpdate` against the entry they read) rather than
read-append-write:

- indexing lost documents whenever two requests indexed at the same moment — both read the
  same list and both wrote back a version missing the other's addition;
- the corpus-name uniqueness check was "scan the values, then insert", which two concurrent
  requests could both pass. It is now claimed atomically through `TryAdd` on a lower-cased
  name index.

A lock was deliberately not used: the whole mutation is a single dictionary swap, so there
is no critical section to hold.

**Acceptance.**
- [x] 8 writers × 25 documents concurrently → all 200 present, and `DocumentCount` matches
- [x] A reader looping on `ListCorpora` while four writers index → no exception
- [x] Concurrent index and delete converge; deleted documents stay deleted and the count
      describes what is actually stored
- [x] 32 concurrent creates of the same name → exactly one succeeds

---

## W4-08 · Container health check calls a binary the image does not have · **P1** · ✅ fixed (runtime check outstanding)

**Problem.** `Dockerfile:57-58` and `docker-compose.yml:74` run a `wget` health check,
but the build stage purges `curl` (`:34`) and never installs `wget`; the
`mcr.microsoft.com/dotnet/aspnet:10.0` Debian-slim base is not expected to contain either.
If so the container is permanently "unhealthy" and a restart-policy-based orchestrator
would restart it in a loop.

**Fix (done).** `wget` is now installed explicitly in the NodeSource block of
`src/Delibera.Server/Dockerfile` alongside `curl`, with a comment recording why. `curl` is
still purged afterwards (`apt-get purge -y curl`), so the check keeps the binary it needs
while the setup-only downloader does not bloat the final image. The health check itself
(`HEALTHCHECK … CMD wget -qO- http://localhost:8080/api/v1/health`) was already pointing at
the correct route and needed no change.

**Acceptance.**
- [x] The binary the health check runs is installed in the image and not purged afterwards
- [ ] Built image reports `healthy` against a running compose stack *(still unverified — this
      needs `docker compose up` plus `docker inspect`; it has not been executed here)*

---

## W4-09 · MCP tool builds JSON by concatenation · **P1** · ✅ done

**Problem.** `Mcp/DeliberaMcpTools.cs:84` assembles the response as a JSON string with an
**unescaped** `ex.Message` interpolated into it. An error message containing a quote or a
backslash produces invalid JSON for the model, and a crafted message could inject fields.
The same tool also clamps nothing: `maxRounds` is documented as "1-10" but unvalidated
(`:29-47,60-102`), unlike the HTTP path which at least has a validator for one contract.

**Fix (partially done).** Both string-built payloads now go through `JsonSerializer` via a
private `ErrorPayload(message)` helper, and `maxRounds` is `Math.Clamp(…, 1, 10)` as the tool
description always promised. `strategy` now falls back to `"Standard"` for null, matching the
HTTP path (this also cleared CS8601).

**Acceptance.**
- [x] Error payloads serialised — an exception message containing quotes or backslashes can no
      longer produce invalid JSON
- [x] `maxRounds` clamped to 1–10; `strategy ?? "Standard"`
- [ ] Test that feeds a quote/backslash-laden error message through the tool and parses the
      result (the tool needs an orchestration service, so this is a small fixture test)

---

## W4-10 · SSE robustness · **P2** · 🟡 partial — SSE writer done, channel still unbounded

**Problem.** `SseDebateStreamWriter` writes no heartbeat and no `retry:` hint, so any
intermediary proxy closes an idle stream; when the stream ends without a terminal event
the response just stops. `LocalDebateOrchestrator.cs:234` and
`RedisDebateOrchestrator.cs:402` use unbounded channels, so a slow consumer grows memory
without limit.

**Fix (partially done).** `SseDebateStreamWriter` now opens with a `retry: 3000` hint, emits a
keep-alive comment whenever the stream has been silent for `DefaultHeartbeatInterval`
(15 s), and always terminates: `debate-completed` on success (`:137`), `debate-error` on
failure (`:144`), and `debate-error` from the `finally` path when the stream ends without a
terminal event (`:180`), guarded by `terminalWritten` so the terminal event is delivered
exactly once. The same rewrite fixed the 10.4.0 heartbeat pump defect: one
`MoveNextAsync` per iteration, compared by reference, with any pending move settled before
disposal.

**Still open.** The third part of this finding is **not** done: the debate round channels are
still unbounded. `LocalDebateOrchestrator.cs:251` and `RedisDebateOrchestrator.cs:419` both
call `Channel.CreateUnbounded<DebateRound>()`. A consumer that stops draining (a client that
stalls without closing, a worker that is not reading) therefore grows memory without limit.
Bounding them is a behavioural change — a full channel must block or drop the producer — and
is left for a separate decision rather than folded into an SSE-writer fix.

**Acceptance.**
- [x] Client always receives a terminal event, including on failure — pinned by the
      SseDebateStreamWriter tests
- [ ] An idle SSE connection survives a real proxy timeout *(not exercised here; the 15 s
      heartbeat and `retry:` hint are in place, but no proxy sits in front in this environment)*
- [ ] Debate round channels are bounded, with a defined full-channel behaviour
