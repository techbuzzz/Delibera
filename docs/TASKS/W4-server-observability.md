# W4 — Server, Redis, Observability

> **Goal:** the HTTP/MCP surface tells the truth about what the service does, and nothing
> runs that the operator cannot see.
> **Verification note:** findings marked *(unverified)* come from static reading only —
> the container/HTTP behaviour has not been executed yet. Confirm before changing code.
> **Status:** 2 / 10 done (W4-01, W4-09)

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

## W4-02 · Unhandled exceptions produce bare 500s · **P1** · ⬜ todo

**Problem.** The pipeline has neither `AddProblemDetails()` nor `UseExceptionHandler()`.
Exceptions thrown by endpoints (e.g. `DebateOrchestrationService.cs:186`) return an empty
500; in Development the auto-added developer exception page returns stack traces.
`DebateEndpoints.cs:24` advertises `ProducesProblem(503)` that the code never produces.
`GET /debates/{id}/result` returns 409 as `text/plain`, not `ProblemDetails`.

**Fix.** `AddProblemDetails()` + `UseExceptionHandler()`, `ProblemDetails` for the 409,
and either produce the documented 503 or remove the claim.

**Acceptance.**
- [ ] Every documented status code is either produced or removed from the contract
- [ ] Test: a forced endpoint exception returns a `ProblemDetails` body, no stack trace

---

## W4-03 · Serilog is a dependency but never wired · **P1** · ⬜ todo

**Problem.** `CorrelationIdMiddleware.cs:19` pushes a Serilog `LogContext` property, and
`Serilog.AspNetCore 10.0.0` is referenced — but `UseSerilog(...)` appears nowhere in
`Program.cs`. The correlation id is therefore present in no log line, while the response
header suggests otherwise. The package is dead weight.

**Fix.** Either wire Serilog (bootstrap from configuration, `UseSerilog`), or drop the
package and replace `LogContext` with `ILogger.BeginScope`. Given the project's logging
convention, wiring is the right call.

**Acceptance.**
- [ ] A request's correlation id appears in every log line it produces (test with a
      captured sink)
- [ ] No secret (API key, connection string) is ever written by the new pipeline

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

## W4-05 · FluentValidation covers 1 of 4 request contracts · **P1** · ⬜ todo

**Problem.** A single validator exists (`CreateDebateRequest`). `ScenarioRequest`,
`CreateCorpusRequest` and `IndexDocumentRequest` have none, and `ValidationFilter.cs:19`
silently passes them through. Consequences: `POST /scenarios/validate` dereferences a null
on `{}` (NRE → 500), and `maxRounds` is unbounded — an anonymous caller can request an
arbitrary number of paid LLM rounds (see W1-04 on the absence of authentication).

**Fix.** Validators for the three missing contracts, including a `maxRounds` range.

**Acceptance.**
- [ ] `{}` on every endpoint returns 400, not 500
- [ ] `maxRounds` outside the documented range is rejected

---

## W4-06 · Documented 400, actual 422 · **P2** · ⬜ todo

**Problem.** `ValidationFilter.cs:25-27` returns **422 Unprocessable Entity** while every
endpoint declares `.ProducesValidationProblem()` (400). Generated clients will be built
against a status the service never returns.

**Fix.** Align on one status. 400 is the ASP.NET Core convention and matches the declared
contract, so change the filter.

**Acceptance.**
- [ ] The filter's status and every `.ProducesValidationProblem()` agree

---

## W4-07 · `CorpusService` singleton is not thread-safe · **P1** · ⬜ todo

**Problem.** `CorpusService` is registered as a singleton (`ServerServiceExtensions.cs:28`)
and mutates a plain `Dictionary` + shared `List` (`CorpusService.cs:15,23,43,64,67`).
`ListCorpora()` enumerates `_store.Values` while a request adds to it; the read-modify-write
at `:67` loses updates under concurrency.

**Fix.** `ConcurrentDictionary` for the store, and a lock (or `ImmutableList` copy-on-write)
for the ordered list.

**Acceptance.**
- [ ] Concurrent test: N parallel creates + lists → no exception, no lost corpus

---

## W4-08 · Container health check calls a binary the image does not have · **P1** *（unverified）* · ⬜ todo

**Problem.** `Dockerfile:57-58` and `docker-compose.yml:74` run a `wget` health check,
but the build stage purges `curl` (`:34`) and never installs `wget`; the
`mcr.microsoft.com/dotnet/aspnet:10.0` Debian-slim base is not expected to contain either.
If so the container is permanently "unhealthy" and a restart-policy-based orchestrator
would restart it in a loop.

**Fix.** Install the binary the check actually uses (or use the .NET SDK-free approach:
a tiny `curl`-free check such as a TCP probe), then build the image and confirm
`docker inspect` reports `healthy`.

**Acceptance.**
- [ ] Built image reports `healthy` against a running compose stack *(must be executed —
      currently unverified)*

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

## W4-10 · SSE robustness · **P2** · ⬜ todo

**Problem.** `SseDebateStreamWriter` writes no heartbeat and no `retry:` hint, so any
intermediary proxy closes an idle stream; when the stream ends without a terminal event
the response just stops. `LocalDebateOrchestrator.cs:234` and
`RedisDebateOrchestrator.cs:402` use unbounded channels, so a slow consumer grows memory
without limit.

**Fix.** Periodic comment heartbeat on idle, a terminal `debate-error`/completion event
in `finally`, and a bounded channel sized to the expected round count.

**Acceptance.**
- [ ] An idle SSE connection survives a proxy timeout
- [ ] Client always receives a terminal event, including on failure
