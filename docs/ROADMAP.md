# 🗺️ Delibera — Multi-Version Roadmap

> **Current stable:** `10.5.1`  
> **Active branch:** `feat/v10.5.1`  
> **Document date:** October 2026  
> **Versioning:** `NET_MAJOR.FEATURE.PATCH` — first digit matches the target .NET runtime (`10` = .NET 10, `11` = .NET 11). Breaking changes are allowed at .NET-major boundaries only.

> **Note on 10.5.1.** This release carried six features in a PATCH slot, which does not match the
> convention above — FEATURE work normally lands in a FEATURE version. It was chosen deliberately, and
> the no-breaking-change rule is what keeps the SemVer contract consumers depend on intact. The
> features originally planned for 10.4.0 and 10.5.0 therefore all shipped in 10.5.1, which is why both
> of those rows below read as released.

---

## 📋 Version Overview

| Version | Theme | Key Deliverables | Status |
|---------|-------|-----------------|--------|
| **10.2.6** | _Feature Bundle_ | F-01 Streaming, F-02 Voting, F-03 Persistence, F-04 Memory, F-05 Structured Output, F-07 Templates, F-08 OpenTelemetry, F-09 Adaptive Strategy, F-10 Quick Wins | ✅ Released |
| **10.2.7** | _Multi-Modal_ | F-06 Vision + Document Attachments | ✅ Released |
| **10.3.0** | _Platform Release_ | Breaking-change cleanup, Distributed Debates, Result Caching, Delibera.Server, Delibera.Redis | ✅ Released |
| **10.4.0** | _Measured Round_ | SSE defect fixes, context compression wired up, correctness pass — 9 defects fixed | ✅ Released |
| **10.5.0** | _Scale & Reliability_ | Measured round published; W2-15 index duplication measured but not yet fixed | ✅ Released |
| **10.5.1** | _Close the Loop_ | Tool use (#14), debate diff (#15), CLI (#16), cost gates (#18), gRPC (#11), NuGet GA (#13), W2-15 fixed, W2-16 documented | ✅ Released |
| **10.6.0** | _Harden the Edges_ | Vector pruning (filter-delete on `IVectorStore`), bounded round channels (W4-10), W2-13/W2-14 product decisions | 🔷 Planned |
| **11.0.0** | _.NET 11 Migration_ | Target .NET 11, C# 14 idioms, performance tuning for new runtime | 🔶 Future (.NET 11 GA) |

---

## ✅ v10.2.6 — Feature Bundle

> _Theme: Core debate engine — streaming, voting, memory, observability, and DX_

Released 2026-07-06. Nine features in one coordinated release (F-06 deferred to v10.2.7).

### F-01 · Async Streaming Council

`IAsyncEnumerable<DebateRound> StreamDebateAsync(CancellationToken)` on `ICouncilExecutor`.  
`DebateRound` gains `Total`, `Duration`, `IsFinal`. `ExecuteAsync()` backward-compatible.  
Example: `StreamingCouncilExample.cs`

### F-02 · Pluggable Vote / Consensus Engine

`IVotingStrategy` + `MajorityVotingStrategy`, `BordaCountStrategy`, `WeightedVotingStrategy`.  
`Chairman.CreateVoting(IVotingStrategy)` factory. `DebateResult.VotingTally`.  
Example: `VotingExample.cs`

### F-03 · Debate Persistence & Resume

`IDebateStore`, `FileDebateStore` (atomic JSON write-then-rename), `InMemoryDebateStore`.  
`CouncilBuilder.WithPersistence(IDebateStore)` + `.ResumeFrom(debateId)`. `DebateResult.DebateId`.  
Example: `PersistenceExample.cs`

### F-04 · Agent Memory & Long-Term Context

`IAgentMemory` — `StoreAsync` / `RecallAsync` / `DeleteAsync`.  
Built-in: `InMemoryAgentMemory`, `QdrantAgentMemory`, `PgVectorAgentMemory`.  
`CouncilBuilder.WithAgentMemory(IAgentMemory)`.  
Example: `AgentMemoryExample.cs`

### F-05 · Structured Output / JSON Schema Enforcement

`ICouncilExecutor.ExecuteTypedAsync<T>()` + `CouncilBuilder.WithStructuredOutput<T>()`.  
`JsonSchemaOutputSerializer` with automatic retry on deserialization failure.  
Example: `StructuredOutputExample.cs`

### F-07 · Debate Templates & Presets Library

`DebateTemplate` static class — `ArchitectureReview`, `RiskAssessment`, `CodeReview`, `ProductDecision`, `SecurityAudit`, `DataArchitecture` + `Custom()` escape hatch.  
Example: `TemplatesExample.cs`

### F-08 · OpenTelemetry Observability

`DeliberaActivitySource` + `DeliberaMeter` + `DeliberaTelemetry` facade.  
Spans: `CouncilExecute`, `CouncilRound`, `MemberRespond`, `RagQuery`, `Compression`, `OperatorExecute`, `ChairmanSynthesize`, `ChairmanOpen`, `PersistenceSaveCheckpoint`, `PersistenceLoadCheckpoint`.  
Metrics: `DebateDuration`, `RoundDuration`, `TokensTotal`, `DebatesCompleted`, `CompressionRatio`.  
`CouncilBuilder.WithTelemetry(TelemetryOptions)`.  
Example: `TelemetryExample.cs`

### F-09 · Dynamic Strategy Switching

`IStrategySelector` with `SelectNextAsync(DebateProgress, CancellationToken)`.  
Built-in: `AdaptiveStrategySelector` (stalemate → escalate). `DebateRound.StrategyUsed` audit trail.  
Example: `AdaptiveStrategyExample.cs`

### F-10 · Quick Wins Bundle

| Feature | Description |
|---------|-------------|
| **F-10a** HTML Export | `DebateResult.ToHtml()` / `SaveToHtmlAsync()` — Dark/Light themes, collapsible rounds, embedded CSS |
| **F-10b** `WithTimeout(TimeSpan)` | `CouncilBuilder.WithTimeout(TimeSpan)` — internal `CancellationTokenSource` linked to caller's token |
| **F-10c** Persona Presets | `Persona` static class — `Expert`, `DevilsAdvocate`, `CautiousOptimist`, `DataDrivenAnalyst`, `RiskManager`, `Pragmatist` |
| **F-10d** Benchmark Mode | `CouncilBenchmark` — `AddConfiguration`, `WithQuestion`, `RunAsync()` → `BenchmarkReport.SaveComparisonAsync()` |
| — | `CouncilBuilder.WithParticipantLimit(int)` — guard misconfiguration |

---

## ✅ v10.2.7 — Multi-Modal

> _Theme: Bring images, diagrams, and documents into the debate_

Released 2026-07-07.

### F-06 · Multi-Modal Council (Vision + Documents)

`MemberCapabilities` flags enum (`Text`, `Vision`). `FileAttachment` + `BinaryAttachment`.  
`IFileContentReader` + `FileContentReaderRegistry` with built-in `PlainTextFileReader`, `ImageFileReader`, `FallbackFileReader`.  
`CouncilBuilder.WithAttachment()` + `WithFileReader()`. Vision auto-detection via `ModelContextWindowRegistry.SupportsVision()`.  
Example: `MultiModalExample.cs`

---

## ✅ v10.3.0 — Platform Release

> _Theme: Delibera as a platform, not just a library_

Released 2026-07-17.

### P-01 · Breaking-Change Cleanup

- Rename `Moderator` → `Chairman` across all public APIs
- Remove `IDebateStrategyWithOptions` — unified `IDebateStrategy.ExecuteAsync` signatures
- `ModelCapabilities` is now non-nullable; use `ModelCapabilities.IsUnknown` sentinel
- Rename `RagProviderFactory` → `VectorStoreFactory`
- Fix `WeightedVotingStrategy.ResolveWeight` edge case
- Remove `DebateStatus.Paused` and `DebateOrchestrationStatus.Pending`
- Fix `DebateRecord.Label` default value
- Rewrite `SseDebateStreamWriter` to use `IDebateOrchestrator.StreamAsync()`
- Remove `DebateRecord._channel`, `RoundWriter`, `RoundReader`
- Rewrite `DebateOrchestrationService` to use event-driven streaming
- Fix `FakeLLMProvider` + `TemplateRegistryTests` for new API surface

---

### S-01 · Distributed Debates

- `IDebateOrchestrator` interface — assign rounds to workers, collect results
- `LocalDebateOrchestrator` — single-process orchestrator for local execution
- `RedisDebateOrchestrator` — Redis pub/sub for round dispatch + result collection
- `DebateHandle` — opaque handle for tracking debate execution
- `DebateOrchestrationStatus` — status enum for orchestration lifecycle
- `DebateRoundEvent` — event model for round completion notifications
- `DebateWorkerService` — background worker that picks up and processes debate rounds
- `RedisOrchestratorOptions` — configuration for Redis-based orchestration
- `RedisOrchestratorExtensions` — DI registration helpers for Redis orchestration
- `ICouncilBuilder.WithOrchestrator(IDebateOrchestrator)` fluent API

---

### S-03 · Result Caching

- `IDebateCache` interface — `GetAsync(CacheKey)` / `SetAsync(CacheKey, DebateResult)`
- `CacheBehavior` enum: `UseCache`, `BypassCache`, `RefreshCache`
- `DebateCacheKeyGenerator` — deterministic key from question + config + model versions
- `InMemoryDebateCache` — in-process LRU cache
- `FileDebateCache` — file-system cache with atomic writes
- `RedisDebateCache` — Redis-backed distributed cache
- Cache metadata on `DebateResult` (`CacheHit`, `CacheKey`, `CachedAt`)
- `ICouncilBuilder.WithCacheBehavior(CacheBehavior)` / `WithCache(IDebateCache, CacheBehavior)`
- DI extension methods for cache registration
- OpenTelemetry counter: `delibera.cache.hits` / `delibera.cache.misses`

---

### P-02 · Delibera.Server — ASP.NET Core Minimal API

New project: **`Delibera.Server`**

- ASP.NET Core 10 Minimal API hosting Delibera over HTTP
- REST API: `POST /api/debates` (start), `GET /api/debates/{id}` (status), `DELETE /api/debates/{id}` (cancel)
- SSE endpoint: `GET /api/debates/{id}/stream` — powered by `IDebateOrchestrator.StreamAsync()`
- Background queue via `IHostedService` + `System.Threading.Channels`

```csharp
builder.Services.AddDeliberaServer(options =>
{
    options.MaxConcurrentDebates = 5;
    options.RequireAuthentication = true;
});
app.MapDeliberaEndpoints();
```

---

### P-04 · NuGet GA Milestone

- Stable `10.3.0` on NuGet (exit preview)
- `Delibera.Core` — core library (no ASP.NET dep)
- `Delibera.Server` — ASP.NET Core Minimal API
- `Delibera.Redis` — Redis orchestration + caching
- `Delibera.Templates` — presets library (optional)
- Symbol packages + source link for all packages

---

### P-05 · Performance & Integrity

- Memory leak fixes: eviction timers on `LocalDebateOrchestrator`, `DebateOrchestrationService`, `RedisDebateOrchestrator`; `using var ProviderFactory` in all server templates
- `IDisposable` on `CompressionCache` and `FileDebateStore` (dispose `ReaderWriterLockSlim` / `SemaphoreSlim`)
- Thread safety: `volatile int` backing for `DebateRecord.Status` and `DebateEntry.Status`; `ConcurrentDictionary` + `GetOrAdd` in `ProviderFactory.CachingFactory`
- `ConfigureAwait(false)` on all `await` in `Delibera.Core` and `Delibera.Redis` (~50+ sites)
- `ValueTask<T>` on `IDebateCache`, `IDebateStore`, `IDebateOrchestrator.GetStatusAsync` — avoids `Task` allocation for sync paths
- `FrozenDictionary` / `FrozenSet` in `ModelContextWindowRegistry` for O(1) lookups
- `SerializeToUtf8Bytes` in `SseDebateStreamWriter` and `DebateCacheKeyGenerator`
- Single-row Levenshtein with `Span<int>` + `stackalloc`
- `ReaderWriterLockSlim` in `TokenCounter`; `readonly record struct RankedOption`
- `StringBuilder` capacity hints in `DebateResult.ToMarkdown()`

---

## ✅ v10.4.0 / v10.5.0 — Measured Rounds

> _Theme: run it for real, find what is actually broken_

These two releases came from the first end-to-end measurement of Delibera against live cloud models
rather than from a feature plan. Nine defects were found and fixed; the release notes and
[WhatsNew-v10.5.0.md](WhatsNew-v10.5.0.md) carry the details. The item that carried forward is W2-15,
fixed in 10.5.1.

---

## ✅ v10.5.1 — Close the Loop

> _Theme: the six issues that were open, plus the bug the measurements found_

Released 2026-10-07. Every GitHub issue that was open at the start of this cycle is now closed, and the
delivery includes **no breaking changes** — see
[WhatsNew-v10.5.1.md](WhatsNew-v10.5.1.md) for the full account.

| Issue | Delivered | Notes |
|---|---|---|
| **#14** I-01 Tool use | ✅ | `IToolProvider`, `AIFunction`, `ToolCallLog` on `DebateRound` and `DebateResult`. Native function calling where the provider wraps a real `IChatClient`; `[[TOOL: …]]` marker otherwise, because the string-only `AsChatClient` adapter drops `ChatOptions.Tools` entirely. |
| **#15** I-02 Debate diff | ✅ | `DebateResultExtensions.Diff`, round matching by number and member by name, word-level LCS, Markdown + HTML export. `TextSimilarity` is public with an explicit length bound. |
| **#16** I-03 CLI | ✅ | `delibera run \| resume \| compare \| benchmark` on System.CommandLine. |
| **#18** S-02 Cost gates | ✅ | `ICostGate`, `IRateLimiter`, `IModelPricingRegistry`, `CostEstimate`. A denial returns a degraded result rather than throwing. |
| **#13** P-04 NuGet GA | ✅ | Matrix is now `Delibera.Core`, `Delibera.Server`, `Delibera.Redis`. |
| **#11** P-03 gRPC | ✅ | `Delibera.Grpc` + `Delibera.Grpc.Client`, layered **on top of** `IDebateOrchestrator`. Not yet in the publish matrix. |
| **W2-15** | ✅ | Idempotent indexing via deterministic point ids. Migration note in the CHANGELOG. |
| **W2-16** | 📋 | Documented as a roster-selection hazard. No framework change. |

Two findings from the tracker stayed open on purpose, because both need either a breaking change or a
product decision: **W2-13** (`WithCache` on `ICouncilBuilder`) and **W2-14** (cache-hit duration
semantics).

---

## 🔷 v10.6.0 — Harden the Edges

> _Theme: the things that were left open on purpose_

The items 10.5.1 declined to take, each because it needs a decision rather than a patch.

- **Vector pruning** — a document that shrinks leaves its tail chunks behind. Fixing it means a
  filter-delete on `IVectorStore`, which has no default interface implementations today, so it is a
  breaking change. Options: a DIM on `IVectorStore`, or accept the break at a feature-major boundary.
- **Bounded round channels (W4-10)** — the SSE writer is fixed, but `Channel.CreateUnbounded` remains
  in `LocalDebateOrchestrator` and `RedisDebateOrchestrator`. Bounding them requires defining what a
  full channel does: block the producer, drop a round, or fail the debate.
- **W2-13** — `WithCache(CacheBehavior, IDebateCache)` on `ICouncilBuilder`. Same trade-off as above.
- **W2-14** — decide whether `TotalDuration` on a cache hit means provenance or caller wait time, then
  document it. Measured today: 0.0 s actual against 189.0 s reported.
- **Publish the gRPC packages** — `Delibera.Grpc` and `Delibera.Grpc.Client` carry full packaging
  metadata and are one workflow line away from the GA matrix.

---
## 🔶 v11.0.0 — .NET 11 Migration

> _Theme: Target .NET 11 runtime — this version aligns with .NET 11 GA_

This is a **runtime-major** bump. It will ship when .NET 11 is generally available.

- Retarget all projects to `net11.0`
- Adopt C# 14 language features (where beneficial — `field` keyword, extension types, etc.)
- Benchmark and tune for .NET 11 runtime improvements (AOT, JIT changes)
- Remove polyfills/workarounds added for .NET 10 compatibility
- Update all NuGet dependencies to .NET 11-compatible versions
- Full CI/CD pipeline update

**Prerequisite:** .NET 11 GA release

---

## 📐 Architectural Principles (all versions)

### Backward Compatibility Policy

| Version range | Policy |
|---|---|
| `10.x.y` | No breaking changes. New params have `= default`. Obsolete via `[Obsolete]` with migration note. Breaking changes only at feature-major boundaries (10.3.0, 10.4.0, etc.) with documented migration. |
| `11.0.0` | Runtime-major bump. Ships with .NET 11 GA. Breaking changes allowed for runtime alignment. |

### Definition of Done (every feature)

- [ ] Public API has XML doc comments
- [ ] `CancellationToken` propagated through all new async paths
- [ ] Unit tests ≥ 80% coverage of new code paths
- [ ] ConsoleApp demo with `--flag`
- [ ] `README.md` + `README-RU.md` updated
- [ ] `CHANGELOG.md` entry following Keep a Changelog format
- [ ] No breaking changes to `CouncilBuilder` fluent API (within feature-minor versions)

### Branching Convention

```
develop                        ← integration branch
feature/p-02-server            ← one branch per feature
feature/i-01-tool-use
release/10.3.0                 ← release stabilization branch
```

---

## 📦 Release Schedule (indicative)

| Version | Estimated | Status |
|---------|-----------|--------|
| 10.2.6 | 2026-07-06 | ✅ Released |
| 10.2.7 | 2026-07-07 | ✅ Released |
| 10.3.0 | 2026-07-17 | ✅ Released |
| 10.4.0 | 2026-10-05 | ✅ Released |
| 10.5.0 | 2026-10-06 | ✅ Released |
| 10.5.1 | 2026-10-07 | ✅ Released |
| 10.6.0 | Q4 2026 | 🔷 Planned |
| 11.0.0 | .NET 11 GA | 🔶 Future |

---

*Delibera Roadmap · October 2026 · [techbuzzz/Delibera](https://github.com/techbuzzz/Delibera)*