# Delibera · Stabilization & Performance Plan (v10.3.1 → v10.4.0)

> **Author:** AI assistant (with @techbuzzz input)
> **Date:** October 2026
> **Branch:** `feature/v10.3.1`
> **Baseline (v10.3.1 as found):** `dotnet build -c Release` → **11 errors** (NU1605) / 15 warnings; tests could not run at all
> **Current:** **0 errors / 0 warnings** (also clean under `-warnaserror`); `dotnet test` → **483 passed** (381 Core + 102 Server)
> **Status:** In progress

---

## 1. Why this plan exists

v10.2.6 → v10.3.0 added nine features, distributed orchestration, result caching, a
Minimal-API server and an MCP endpoint. Each piece was finished in isolation and each
piece has its own test suite — but the pieces were never made to fit *together*. The
result is a class of defects that no single feature's tests can catch:

1. **Tests pass with fakes, production breaks with real I/O.** The fakes complete
   synchronously (`Task.FromResult`), so code paths that only work when a debate never
   suspends are green. A real provider awaits network I/O, and the same code path is
   dead. (Proven: `F-01` — the SSE streaming path yielded **0 of 2** rounds.)
2. **One module's optimisation breaks another's invariant.** `TokenCounter` memoisation
   is a hot-path win and a global lock at the same time; the caching layer added in
   10.3.0 hashes the entire knowledge base twice per debate.
3. **Correctness only on the happy path.** Synchronous callbacks force
   sync-over-async, which converts `OperationCanceledException` into
   `AggregateException` and then swallows it as a domain error.
4. **Concurrency was designed for one debate at a time.** `DebateRecord.Rounds` is a
   plain `List<T>` written by three threads; a `lock` field exists but is never used.

The plan is therefore ordered by **risk of being wrong in production**, not by
comfort: W1 correctness → W2 performance → W3 architecture → W4 server → W5 gates.

---

## 2. Ground rules

| Rule | Rationale |
|------|-----------|
| **Safe fixes ship in 10.3.1; breaking API changes are planned for 10.4.0** (owner decision, 2026-10-02) | A patch release must not break consumers. Anything that needs a signature change is written up as a 10.4.0 task, not silently shipped. |
| **Every claim carries `file:line`.** No task without evidence | Prevents "improvements" based on impressions |
| **Every task has a failing-then-passing proof** | A fix without a regression test is a guess that happened to work |
| **No layer duplication.** Reuse the existing cache/DI, don't add a parallel one | AGENTS.md constraint |
| **One task = one commit** (conventional commit prefix) | Reviewable, revertible |
| **No migration, commit, push or deploy without owner approval** | AGENTS.md constraint |

---

## 3. Waves

| Wave | Theme | Tasks | Risk if ignored |
|------|-------|-------|-----------------|
| [W0](W0-toolchain.md) | Build & toolchain | 3 | Nothing builds for a new contributor; drift returns |
| [W1](W1-correctness.md) | Correctness & stability | 10 | Streaming, DI, cancellation and multi-tenant are broken in production |
| [W2](W2-performance-core.md) | Core performance | 9 | Core is slower than it needs to be exactly where it matters |
| [W3](W3-api-architecture.md) | API & architecture | 8 | API lies, god-class keeps growing, 27-arg constructor |
| [W4](W4-server-observability.md) | Server, Redis, observability | 10 | API returns wrong status codes, no logs, no tenant isolation |
| [W5](W5-quality-gates.md) | Tests, gates, docs | 7 | The same defect class returns unnoticed |

---

## 4. Master table

Legend: ✅ done · 🔄 in progress · ⬜ todo · 🔒 10.4.0 (breaking) · ❓ needs owner decision

### W0 — Build & toolchain
| ID | Task | Pri | Status |
|----|------|-----|--------|
| W0-01 | Align NuGet versions across test projects (NU1605 broke the build) | P0 | ✅ |
| W0-02 | Bump `Microsoft.SourceLink.Git` — NU1902 advisory | P1 | ✅ |
| W0-03 | Single version source (CPM) so drift cannot return | P2 | 🔒 10.4.0 |

### W1 — Correctness & stability
| ID | Task | Pri | Status |
|----|------|-----|--------|
| W1-01 | `StreamDebateAsync` yielded 0 rounds for any async provider | **P0** | ✅ |
| W1-02 | `UseInMemoryCache` throws at resolve — `AddMemoryCache()` never called | **P0** | ✅ |
| W1-03 | Redis worker busy-loops on an idle stream; `BlockMs` unused | **P0** | ✅ |
| W1-04 | No tenant isolation in `DebateEndpoints` (cross-tenant read/cancel/export) | **P0** | ✅ |
| W1-05 | `DebateRecord.Rounds` plain `List<T>` + unused `_lock` → race in SSE | P1 | ✅ |
| W1-06 | Cancelled debates reported as persistence errors (`OCE` → `AggregateException`) | P1 | ✅ |
| W1-07 | SSE emits every round twice (snapshot replay + live stream) | P1 | ✅ |
| W1-08 | `DeliberaServerOptions.SectionName` never matches config → all options dead | P1 | ✅ |
| W1-09 | Redis streams grow without bound; state keys have no TTL | P1 | ✅ |
| W1-10 | `RedisDebateCache` ignores `CancellationToken` on every call | P2 | ✅ |

### W2 — Core performance
| ID | Task | Pri | Status |
|----|------|-----|--------|
| W2-01 | `TokenCounter` takes a **write lock on every cache hit** (shared static) | **P0** | ✅ |
| W2-02 | Knowledge Keeper performs the **same RAG search twice per round** | **P0** | ✅ |
| W2-03 | `store.ListAsync()` + linear scan on **every round** (checkpointing) | P1 | ✅ |
| W2-04 | Response diversity = O(n²) Levenshtein over full texts | P1 | ✅ |
| W2-05 | Log strings interpolated before the level check | P1 | ⬜ |
| W2-06 | Cache key computed twice per debate + chairman/persona missing from it | P1 | ✅ |
| W2-07 | `YandexGptProvider`: throwaway `HttpRequestMessage` + double payload copy | P2 | ⬜ |
| W2-08 | `DebateScenario` operator regex runs on every response even with no operator | P2 | ✅ |
| W2-09 | Fan-out without `MaxDegreeOfParallelism` cap (`:58-71`) | P2 | ✅ |

### W3 — API & architecture
| ID | Task | Pri | Status |
|----|------|-----|--------|
| W3-01 | Remove dead `WeightedVotingStrategy(defaultWeight)` parameter | P1 | ✅ |
| W3-02 | Remove dead field `CouncilBuilder._persistedOptionsSnapshot` | P2 | ✅ |
| W3-03 | `MemoryEntry.Metadata` nullability (CS8602/CS8604) | P1 | ✅ |
| W3-04 | `CouncilOptionsSnapshot` drops 5 of its documented settings | P1 | ⬜ |
| W3-05 | `CouncilBuilder` doc claims precedence that the code does not implement | P2 | ⬜ |
| W3-06 | `CouncilExecutor` decomposition (27-arg ctor, 1332 lines) | P1 | ⬜ |
| W3-07 | Async round callback to remove the last sync-over-async | P1 | 🔒 10.4.0 |
| W3-08 | `IDebateStrategy` signature / `IRagProvider` vs `IVectorStore` overlap | P2 | 🔒 10.4.0 |

### W4 — Server, Redis, observability
| ID | Task | Pri | Status |
|----|------|-----|--------|
| W4-01 | `WithOpenApi` deprecated in .NET 10 (ASPDEPR002 × 4) | P1 | ✅ |
| W4-02 | `AddProblemDetails` + `UseExceptionHandler` missing | P1 | ✅ |
| W4-03 | Serilog never wired — `Serilog.AspNetCore` is a dead dependency | P1 | ✅ |
| W4-04 | Custom metrics have no fallback exporter → silently lost | P1 | ⬜ |
| W4-05 | FluentValidation covers 1 of 4 request contracts | P1 | ✅ |
| W4-06 | `ValidationFilter` returns 422 while OpenAPI documents 400 | P2 | ✅ |
| W4-07 | `CorpusService` singleton mutates a plain `Dictionary`/`List` | P1 | ✅ |
| W4-08 | Dockerfile HEALTHCHECK calls `wget` that the image does not contain | P1 | ⬜ |
| W4-09 | MCP tool JSON built by string concatenation (unescaped) | P1 | ✅ |
| W4-10 | SSE: no heartbeat, no terminal event, unbounded channel | P2 | ⬜ |

### W5 — Tests, gates, docs
| ID | Task | Pri | Status |
|----|------|-----|--------|
| W5-01 | Fix the 15 build warnings, then enable `TreatWarningsAsErrors` | P1 | ✅ |
| W5-02 | Tests for uncovered code (`AutoChunker`, `CompressionCache`, `ServiceCollectionExtensions`) | P1 | ⬜ |
| W5-03 | Concurrency tests for orchestrator eviction / `Dispose` / `volatile` status | P1 | ⬜ |
| W5-04 | CI runs build + test on every PR | P1 | ✅ |
| W5-05 | "Async fake" test provider to close the fake-vs-real gap | **P0** | ✅ |
| W5-06 | CHANGELOG entry with the breaking changes of this cycle | P1 | ⬜ |
| W5-07 | `docs/` sync: README, QuickStart, `caching.md`, `Server.md` | P2 | ⬜ |

---

## 5. The recurring lesson behind W1-01 and W5-05

Both W1-01 and W5-05 exist because of the same blind spot. `FakeLLMProvider` returns
`Task.FromResult`, so a debate never suspends, so a callback that is only valid for the
duration of a synchronous run survives long enough to look correct. The fix is not one
`GetAwaiter()` — it is making "the code must survive a real suspension point" a
standing property of the test suite:

> Any test that exercises a callback, event or continuation must use a provider that
> yields at least once before completing.

---

## 6. Definition of done for the plan

- [ ] `dotnet build -c Release` → 0 errors, **0 warnings**
- [ ] `TreatWarningsAsErrors=true` in CI
- [ ] `dotnet test` → green, with a regression test for every W1 item
- [ ] No `GetAwaiter().GetResult()` / `.Result` / `.Wait()` in `src/` outside `Dispose`
- [ ] No `new Regex(` in a hot path (already true — keep it true)
- [ ] No mutable `List<T>` shared between the debate loop and a request thread
- [ ] CHANGELOG documents every breaking change since v10.3.0
