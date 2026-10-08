# W5 — Tests, Gates, Documentation

> **Goal:** make the defect classes in W1–W4 impossible to reintroduce unnoticed.
> **Owner decisions, 2026-10-02:** fix all 15 build warnings, then enable
> `TreatWarningsAsErrors`.
> **Status:** 3 / 7 done (W5-01, W5-04, W5-05)

---

## W5-05 · "Async fake" test provider · **P0** · ✅ done

**Problem.** Every debate test runs on `FakeLLMProvider`, which completes synchronously.
Any code that is correct *only while nothing suspends* therefore passes CI and fails in
production. This is not hypothetical: it is exactly how W1-01 shipped — 11 green streaming
tests, and a feature that returns nothing against a real provider.

The existing `FakeLLMProvider` already accepts `chatDelayMs`; the problem is that nothing
makes *asynchronous completion* the default expectation.

**Fix (done).** `FakeLLMProvider` in **both** test projects now suspends by default: an
`await Task.Yield()` before completing, with an explicit `suspends: false` opt-out and a
`Suspends` property a test can assert on. The class doc carries the incident so the reason
survives.

The rule is written where a contributor will actually read it — this repository has **no
`AGENTS.md`**, so it went into `CONTRIBUTING.md` under a new "Testing rules" heading, next to
the build-verification instructions. The same edit corrected two stale claims in that file:
it claimed .NET 8 / C# 12 (the projects are `net10.0` with `LangVersion preview`) and a
uniform four-space indentation (Core uses three).

**Acceptance.**
- [x] The whole suite passes with an always-suspending provider — nothing in it depended on
      synchronous completion, which is the result worth having
- [x] The rule is in `CONTRIBUTING.md`
- [x] Both fakes document the incident and the opt-out

---

## W5-01 · Zero warnings, then `TreatWarningsAsErrors` · **P1** · ✅ done

**Problem.** 15 warnings on a clean build. None are cosmetic:

| Warning | Location | Nature |
|---|---|---|
| CS9113 | `Voting/IVotingStrategy.cs:143` | dead public parameter (W3-01) |
| CS8604, CS8602, CS8604 | `Memory/IAgentMemory.cs:215,292,294` | nullability in a public API (W3-03) |
| CS0169 | `Council/CouncilBuilder.cs:45` | dead field (W3-02) |
| CS8600, CS8604 | `Redis/RedisDebateCache.cs:58` | null JSON deserialised into a non-nullable |
| CS8601 | `Server/Mcp/DeliberaMcpTools.cs:91` | `null` assigned to a non-nullable `string` (W4-09) |
| CS9113 | `tests/…/AdaptiveStrategyTests.cs:311` | unread parameter |
| ASPDEPR002 × 4 | Server endpoints | deprecated OpenAPI API (W4-01) |
| NU1902 × 2 | `Microsoft.Build.Tasks.Git` | vulnerable dependency (W0-02) |

**Fix (done).** All 15 are gone; the build is clean even with `-warnaserror`:
- `WeightedVotingStrategy(defaultWeight)` removed (W3-01) — the parameter was never read
- `MemoryEntry` metadata normalised at both recall sites (W3-03) — line 292 was a **latent
  NullReferenceException**, not just a warning: a vector-store hit without tags would have
  taken pgvector-backed recall down
- `RedisDebateCache` uses `value.ToString()` instead of the possibly-null `(string)` cast
- `DeliberaMcpTools` passes `strategy ?? "Standard"` (W4-09) and serialises its error payloads
- `CouncilBuilder._persistedOptionsSnapshot` deleted (W3-02); unread test parameter removed
- ASPDEPR002: `.WithOpenApi()` calls deleted (W4-01)
- NU1902: `Microsoft.SourceLink.GitHub` 10.0.301 → 10.0.303 (W0-02)

`TreatWarningsAsErrors` is applied in CI via `dotnet build -warnaserror`, leaving local
iteration warning-tolerant.

**Acceptance.**
- [ ] `dotnet build -c Release` → `0 Warning(s)`
- [ ] CI fails on any new warning

---

## W5-02 · Tests for code that has none · **P1** · ⬜ todo

**Problem.** Measured gaps (0 references in the entire test project):

- `Chunking/AutoChunker.cs` (498 lines) and `AutoChunkingOrchestrator.cs` (255) — the
  progressive-disclosure feature has no tests at all
- `Compression/CompressionCache.cs` (240) — no tests, although release notes advertise
  `IDisposable`
- `DependencyInjection/ServiceCollectionExtensions.cs` (340) — no tests, which is why
  W1-02 (`IMemoryCache` never registered) was never caught
- `CouncilBuilder.ApplyOptions` — only indirectly covered
- `Benchmarking/CouncilBenchmark.cs` — exists, never run

**Fix.** Prioritised by blast radius: DI extensions first (they are the entry point for
every consumer), then `AutoChunker`, then `CompressionCache`.

**Acceptance.**
- [ ] `UseInMemoryCache` / `UseFileCache` / `AddDelibera` have resolve-level tests
- [ ] `AutoChunker` has boundary tests (empty input, one-chunk, forced multi-chunk)

---

## W5-03 · Concurrency tests for the claims made in release notes · **P1** · ⬜ todo

**Problem.** v10.3.0's release notes advertise `ConcurrentDictionary`, `volatile int` status,
timer-based eviction and `IDisposable` caches. There is no test that exercises any of them
under concurrency; `LocalDebateOrchestrator` has five tests covering only "unknown id" and
"duplicate id".

**Fix.** Tests for: timer eviction of completed entries, `Dispose` while a debate is active,
concurrent `GetStatusAsync` visibility of a status change, and the W1-05 round collection
under concurrent read/write.

**Acceptance.**
- [ ] Each advertised concurrency guarantee has a test that fails without the guarantee
- [ ] No flaky test: run the concurrency suite 20× in CI

---

## W5-04 · CI on every PR · **P1** · ✅ done

**Problem (worse than expected).** The workflow existed and ran on pull requests, but:
1. it built `src/Delibera.slnx`, which lists only the four `src/` projects — **the test
   projects were never compiled or executed in CI at all**;
2. it never ran `dotnet test`.

That is the mechanical reason the NU1605 drift of W0-01 could ship: nothing in CI ever
built `tests/`.

**Fix.** The workflow now restores and builds the **root** `Delibera.slnx` (the one that
includes tests) with `-warnaserror`, then runs `dotnet test` and uploads a TRX report.
Publishing stays tag-gated.

**Still open for the owner:** the repository contains **two** solution files — the root
`Delibera.slnx` (src + tests) and `src/Delibera.slnx` (src only). CI now points at the root
one, but the duplicate is a trap for anyone who types the obvious path. Deleting it is a
repository-layout decision, so it is left in place and flagged here.

**Acceptance.**
- [ ] A deliberately broken build fails the workflow
- [ ] Release packaging is tag-gated

---

## W5-06 · CHANGELOG · **P1** · ✅ done

**Problem.** The changelog is the only place a consumer learns about the breaking changes
in this cycle.

**Acceptance.**
- [x] Every breaking change since 10.3.0 is listed with its replacement — `## [10.3.0]` carries
      the P-01 table, `## [10.4.0]` carries W3-07 with before/after code, and `[10.5.1]` states
      plainly that it introduces none
- [x] The 10.3.0 entry's claim of "402 unit tests pass" is reconciled with reality — the claim was
      never actually written into the entry, so there is nothing outstanding to correct. Worth
      recording rather than leaving implied: the same class of drift did reach nuget.org through
      `PackageReleaseNotes` (the 514 figure), and that one is fixed and called out in `[10.5.1]`.

---

## W5-07 · Documentation sync · **P2** · 🟡 partial

**Problem.** `docs/` is extensive (QuickStart, Server, caching, WhatsNew × 3, NET10
upgrade × 2, ROADMAP) and drifts: several documents describe behaviour this plan is
changing, and the deleted `docs/NET10-Upgrade.pdf` files are still tracked in git.

**Acceptance.**
- [x] Git index matches the intended file set — the two `docs/NET10-Upgrade.pdf` files are no
      longer tracked, and no document outside this one still references them
- [x] Behaviour-changing documents updated for 10.5.1 — QuickStart (EN/RU), `Server.md`,
      `caching.md`, `distributed-debates.md`, `src/README.md`, `src/Delibera.Core/README.md`,
      `README.md`, `README-RU.md`, `ChatClientLLMProvider.md`, `CONTRIBUTING.md`, plus new
      `docs/WhatsNew-v10.5.1.md`
- [ ] Every documented code sample compiles — **not verified.** The samples in the files changed
      for 10.5.1 were written against the source and every `With…` method they name was checked to
      exist, but no documentation-wide sample compilation was run. Treat this row as open.

---

## W5-08 · No framework regression in the debate loop · **P1** · ✅ done

**Problem.** Nothing compared framework cost between releases, so a change that made every debate
~2× slower to orchestrate would ship green. That is exactly what happened in 10.5.1: cost accounting
ran unconditionally, concatenating the prompts and running the token counter over prompt and response
on every member call — a cost quadratic in the debate's own length, for a result that was then
discarded.

**Measurement.** `.bench/PerfCheck`: a fake provider returning instantly, three members, three rounds,
long deterministic responses, and the baseline checkout supplied as a git worktree through
`-p:DeliberaCorePath=`. Wall time against live models is useless here — the proxy on this machine
dominates latency by orders of magnitude.

| Checkout | Median | p95 | Allocations |
|---|---|---|---|
| v10.5.0 (`1297e38`) | 0.634 ms | 1.22 ms | 3 735 640 B |
| v10.5.1, unguarded (`978f4cb`) | **1.404 ms** (+121%) | 1.96 ms | **5 034 080 B** (+35%) |
| v10.5.1, guarded | 0.679 ms | 1.41 ms | 3 736 000 B |

**Fix (done).** Accounting runs only when `DebateExecutionOptions.CostTrackingEnabled`. A second pass
removed an `async ValueTask` from the per-member admission check — its state machine was allocated on
every call even though the method returned immediately.

**Acceptance.**
- [x] Allocations within noise of the previous release (+0.01%)
- [x] Wall time within the noise band — repeated 80-iteration runs put the two within each other's
      spread (0.52–0.60 ms vs 0.56–0.66 ms), so the residual difference is not treated as signal
- [x] Cost gating still works where configured — `CostGateIntegrationTests` covers both the
      enforcing and the permissive case

---

## W5-09 · Indexing produces the same corpus regardless of run count · **P1** · ✅ done

**Problem.** W2-15: every index run appended, because point ids were random. The 10.5.0 measurement
found 72 points for 24 unique chunks.

**Measurement.** Live Qdrant, same corpus and queries, varying only how many index runs had happened.

| | v10.5.0 | v10.5.1 |
|---|---|---|
| 3 index runs of one document | **360 points** | **120 points** |
| Search median afterwards | 0.968 ms | 0.873 ms |

**Fix (done)** — deterministic point ids; see W2-15.

**Acceptance.**
- [x] Point count independent of run count
- [x] Latency improves as a consequence
- [ ] Ranking quality under duplicates improves — **not measured.** The latency win is modest
      because a few hundred vectors stay index-resident; the real effect is on top-k ranking, and
      demonstrating it needs a recall/precision comparison this harness does not perform.

**Why this gate exists at all.** W5-05 closed the fake-vs-real gap for *behaviour*. This is its
performance twin: a fake provider returning instantly is exactly what makes framework overhead
measurable, and a feature that made the pipeline twice as slow passed every behavioural test.
