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

## W5-06 · CHANGELOG · **P1** · ⬜ todo

**Problem.** The changelog is the only place a consumer learns about the breaking changes
in this cycle. So far: `WeightedVotingStrategy(double)` removal, the S-01/S-03 additions
in 10.3.0, and the 10.2.6 feature set.

**Fix.** One entry per wave, with a Breaking Changes table in the owner's chosen format
(the P-01 migration table in `docs/scope/P-01-breaking-change-cleanup.md` is the precedent).

**Acceptance.**
- [ ] Every breaking change since 10.3.0 is listed with its replacement
- [ ] The 10.3.0 entry's claim of "402 unit tests pass" is reconciled with reality

---

## W5-07 · Documentation sync · **P2** · ⬜ todo

**Problem.** `docs/` is extensive (QuickStart, Server, caching, WhatsNew × 3, NET10
upgrade × 2, ROADMAP) and drifts: several documents describe behaviour this plan is
changing, and the deleted `docs/NET10-Upgrade.pdf` files are still tracked in git.

**Fix.** After the waves land: update `caching.md` (tenant/cache-key semantics), `Server.md`
(problem details, auth posture), QuickStart (DI examples that actually resolve), and
either restore or intentionally remove the two PDFs with a note.

**Acceptance.**
- [ ] Every documented code sample compiles
- [ ] Git index matches the intended file set
