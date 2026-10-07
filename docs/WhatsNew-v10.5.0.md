# Delibera v10.5.0 — What's New

> **Release type:** minor. New public API (`MemberFailure`, `OllamaEmptyResponseException`,
> `OllamaProvider` options, `DebateExecutionOptions` members). No breaking changes — existing
> builders, strategies and providers compile unchanged.

This release is the result of the first end-to-end measurement of Delibera against real cloud
models. Running multi-model debates for real, rather than reasoning about them, found five defects —
two of which made headline features silently non-functional.

Everything below is measured. Method and caveats:
[performance-measurements.md](performance-measurements.md).

---

## Table of contents

- [The headline](#the-headline)
- [Fixed](#fixed)
- [Added](#added)
- [Measured](#measured)
- [What changed about the documentation](#what-changed-about-the-documentation)
- [Known issues, deliberately not fixed](#known-issues-deliberately-not-fixed)
- [Use cases](#use-cases)
- [Upgrade notes](#upgrade-notes)

---

## The headline

Context compression shipped for two releases advertising **30–70% token savings**. Measured across
three topics, `HybridCompressor` delivers **11.7–12.1%**.

Not because the strategy is bad — because **nothing ever called it**. `CompressTextAsync` was
public API that no code in the library invoked. Ten measured runs with compression enabled reported
**0.00% saved at 0 ms overhead** while context grew past 10,000 tokens in a single round. The
documented range was never validated, and the missing call site made it unreachable.

Two consequences: the feature now works, and the claim is corrected. The old number was not
"optimistic" — it described a code path that did not execute.

The same round found that **`OllamaProvider` could not talk to Ollama Cloud at all**. It defaulted to
`num_predict: -1`, and the endpoint rejects that with `max_tokens must be positive, got: -1`. Every
request failed before generating a token. If you have been testing against local Ollama only, that
is why.

---

## Fixed

### Context compression never ran

`CouncilExecutor.CompressTextAsync` existed, was public, and was never called from the debate
pipeline. `TokenStats` and `CompressionLogs` were never assigned anywhere in Core, so
`DebateResult.TokenStats` was always null — which also meant the server's `SavedByCompression` was
permanently empty.

The compressor is now attached through `DebateExecutionOptions` (the object already threaded into
every strategy, so no interface widened) and applied to each round prompt above a
`CompressionThresholdTokens` floor, default **1,200 tokens** — compressing a small prompt costs more
than it saves. Every attempt is logged, including the ones that did not help.

Compression is best-effort by design: a compressor that throws, returns nothing, or returns *more*
text than it received leaves the prompt untouched and is recorded as such. A debate is worth more
than a few saved tokens.

### Ollama Cloud rejected every request

```csharp
// before
Options = new RequestOptions { Temperature = temperature, NumPredict = _maxOutputTokens }
// _maxOutputTokens defaulted to -1
```

```csharp
// after — "no cap" means the field is omitted, not sent as -1
Options = new RequestOptions
{
    Temperature = temperature,
    NumPredict = _maxOutputTokens > 0 ? _maxOutputTokens : null
}
```

### `TotalDuration` was negative on every debate

`DebateResult.StartedAt` relied on a property initialiser that runs inside
`DebateResultBuilder.Build()` — after `MarkCompleted()` had already stamped `CompletedAt`. So
`CompletedAt - StartedAt` came out below zero, and every debate reported `-0.0s`. Start time is now
captured when the builder is created.

### A failed participant's error text was fed to the Chairman as an opinion

```csharp
// before
catch (Exception ex)
{
    return (member.Role, member.DisplayName, Response: $"[ERROR: {ex.Message}]");
}
```

That string entered the transcript like any other response. A real example captured from a measured
run, verbatim from the Chairman's prompt:

```
**Expert 3: glm-5.3-flash (OllamaCloud):**
[ERROR: Empty response from model 'glm-5.3-flash'.]
```

The verdict was synthesised from a partial council while looking complete. Failed members are now
omitted from the round and recorded on the result.

---

## Added

| Type / member | Purpose |
|---|---|
| `DebateResult.FailedMembers` | `IReadOnlyList<MemberFailure>` — every failed turn, with round, role, model and error |
| `DebateResult.IsDegraded` | `true` when the verdict came from a partial council |
| `MemberFailure` | Failure record; `ToString()` shaped for execution logs and report rows |
| `OllamaEmptyResponseException` | Carries `DoneReason` and `ReasoningChars`; `BudgetConsumedByReasoning` separates the two causes |
| `OllamaProvider(enableThinking:)` | Default `false`. Sends `Think` explicitly so a reasoning model cannot silently eat the answer budget |
| `OllamaProvider(retryOnBudgetExhaustion:)` | Default `true`. Retries once with a larger budget when generation was cut off mid-thought |
| `DebateExecutionOptions.ContextCompressor` / `ContextCompressionOptions` / `ContextCompressionCache` / `CompressionLogs` / `CompressionThresholdTokens` | How the executor reaches the compressor without changing `IDebateStrategy` |

`ContextCompressionWiringTests` — four regression tests pinning that a configured compressor is
actually invoked, that savings reach the result, that an unconfigured run invents nothing, and that
a compressor returning *more* text is not trusted.

---

## Measured

Full method and caveats in [performance-measurements.md](performance-measurements.md).

| | Measured |
|---|---|
| Framework overhead | **0.0 s** — time no model call explains, in every run |
| Debate wall time | 154–209 s (4 rounds, 3 members + chairman) |
| Throughput | 840–1,144 chars/s, flat across all runs |
| Round cost profile | Critique + refinement = 62–77% of a debate |
| Compression | **11.7–12.1%** prompt tokens |
| Knowledge Keeper | 3 retrieval queries per debate, steady across topics |
| Operator (MCP) | 1–3 delegated tasks per debate, scaling with the question |
| Cache hit | **0.0 s** against 154–209 s uncached |
| Output balance | one verbose model produced **68.8%** of all member text |

What this says about where optimisation is worth spending: **not inside the execution pipeline.** The
framework adds no measurable time. Wall time tracks output volume, so the levers are round count,
output caps, and roster composition.

---

## What changed about the documentation

- The **30–70% compression claim is corrected everywhere it appeared** — root README (EN and RU),
  `src/README.md`, `src/Delibera.Core/README.md`, and both QuickStart files.
- New [performance-measurements.md](performance-measurements.md) records what was measured, how,
  and which documented claims the results contradict.
- Both READMEs gained a **Use Cases** table and a **Measured Behaviour** section, and both carry
  the honest caveats: a debate takes 154–209 s, and a single verbose model can dominate the output.
- `docs/TASKS/W2-performance-core.md` gained W2-10 … W2-16 for what the measurement round found.

---

## Known issues, deliberately not fixed

Each of these needs a product decision rather than a patch, so they are documented instead.

- **`TotalDuration` on a cache hit** reports the cached debate's duration, not the caller's wait.
  Measured: a lookup served in 0.0 s reported 189.0 s. Anything computing latency or telemetry from
  that property overstates by roughly two orders of magnitude. Use `CacheHit` / `CachedAt`. The
  question is whether the property should carry provenance or wait time.
- **`WithCache(CacheBehavior, IDebateCache)` is missing from `ICouncilBuilder`.** The interface
  exposes only `WithCacheBehavior`, documented as resolving the cache from DI, so a consumer holding
  the interface cannot supply a backend — either take a dependency on the concrete `CouncilBuilder`
  or run a container.
- **Vector-store indexing is not idempotent.** `IndexFileAsync` appends unconditionally: three runs
  over the same 24 chunks left 72 points, diluting retrieval with exact duplicates. A deployment
  that indexes on startup degrades its own search quality over time. Worth a content hash, or a
  delete-then-replace per document.
- **Documented compression savings (30–70%) are inaccurate.** Measured 11.7–12.1%. Corrected in
  this release, but the strategy itself is still worth revisiting against that lower bound.

---

## Use cases

Scenarios where models that disagree with each other beat one model answering twice. Measured
evidence is cited where it exists.

| Use case | Council shape | Why the council earns its cost |
|---|---|---|
| **Architecture decision** | 3 experts + chairman, 4 rounds, RAG over your ADRs | Different models surface different failure modes, and the Chairman must name where they disagree. Measured: an architecture verdict raised two risks nobody in the council mentioned — a licence change, and the unasked question of *why* the migration was wanted. |
| **Code review before a human** | Code specialist + 2 generalists, 3 rounds | Catches what a linter cannot: a wrong fix, and an unstated assumption. Measured: a cheap roster answered *"name the single most important defect"* with 78,000 characters and still picked the weaker of the two available answers. |
| **Incident post-mortem** | 3 experts, chairman, RAG over runbooks + `chrome-devtools-mcp` for live evidence | Forces the split between "what we know" and "what we assume" into the written record. |
| **Security and compliance review** | 3 experts, chairman, with `IsDegraded` checked | A member that failed is a hole in the review, not a footnote. `IsDegraded` makes that impossible to miss. |
| **Vendor / tool selection** | 3 experts + chairman, criteria in the prompt | Turns a preference argument into an explicit trade-off table with the dissent preserved. |
| **Research with citations** | Knowledge Keeper over your documents, 4 rounds | Measured: retrieval returned the right passage (top score 0.738) and verdicts cited documents by line range. |
| **On-call decision support** | 2–3 experts, 2 rounds, cached | The same incident shape twice costs one debate: a measured cache hit served in **0.0 s** against 154–209 s uncached. |
| **Prompt and agent design review** | 3 experts, chairman | Finds the prompt where the most confident model wins rather than the best-reasoned one. |

**When not to use it:** single-fact lookups, anything where one strong model plus a tool call beats
three opinions, and latency budgets under a second. A measured four-round debate takes 154–209 s.

---

## Upgrade notes

No breaking changes. Two behaviours that were previously broken now work, so anything that worked
around them can be revisited:

- **Compression now costs time it did not before.** It was previously a no-op. Watch
  `DebateResult.TokenStats` (now populated) and `CompressionLogs` when tuning.
- **`DebateResult.TokenStats` is no longer null** when compression is configured. Code that null-checked
  to detect "compression is on" will now always take the non-null path.
- **`DebateResult.FailedMembers` / `IsDegraded`** are new. A debate that previously looked complete
  while missing a participant will now report `IsDegraded == true` — worth alerting on in production.