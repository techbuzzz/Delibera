# 🗺️ Delibera — Multi-Version Roadmap

> **Current stable:** `10.3.0`  
> **Active branch:** `develop`  
> **Document date:** July 2026  
> **Versioning:** `NET_MAJOR.FEATURE.PATCH` — first digit matches the target .NET runtime (`10` = .NET 10, `11` = .NET 11). Breaking changes are allowed at .NET-major boundaries only.

---

## 📋 Version Overview

| Version | Theme | Key Deliverables | Status |
|---------|-------|-----------------|--------|
| **10.2.6** | _Feature Bundle_ | F-01 Streaming, F-02 Voting, F-03 Persistence, F-04 Memory, F-05 Structured Output, F-07 Templates, F-08 OpenTelemetry, F-09 Adaptive Strategy, F-10 Quick Wins | ✅ Released |
| **10.2.7** | _Multi-Modal_ | F-06 Vision + Document Attachments | ✅ Released |
| **10.3.0** | _Platform Release_ | Breaking-change cleanup, Distributed Debates, Result Caching, Delibera.Server, Delibera.Redis | ✅ Released |
| **10.4.0** | _Intelligence & DX_ | Function Calling / Tool Use, Debate Result Diff, CLI improvements | 🔷 Planned |
| **10.5.0** | _Scale & Reliability_ | Rate-limiting & cost gates | 🔷 Planned |
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

## 🔷 v10.4.0 — Intelligence & DX

> _Theme: Smarter debates and better developer experience_

### I-01 · Function Calling / Tool Use

**Why:** Council members should be able to call external tools (search, calculator, database lookup) during a debate round, grounding their arguments in real data.

**What:**
- `IToolProvider` interface — register named tools with JSON Schema descriptors
- `CouncilBuilder.WithTool(IToolProvider)` — fluent API
- `Microsoft.Extensions.AI` `AIFunction` integration (already a dependency)
- Tool calls rendered in `*_result.md` with `<details>` collapsible sections
- Chairman sees tool outputs in synthesis

```csharp
.AddMember("gpt-4o", openai, "Data Analyst", Persona.DataDrivenAnalyst)
.WithTool(new WebSearchTool())
.WithTool(new CalculatorTool())
```

**Effort:** L · 5–8 days

---

### I-02 · Debate Result Diff

**Why:** Running the same question twice with different configurations produces two Markdown reports with no easy way to compare.

**What:**
- `DebateResult.Diff(DebateResult other)` → `DebateDiff` record
- Side-by-side Markdown rendering: agreement points, disagreements, unique arguments per side
- CLI `--diff result1.json result2.json` flag
- `BenchmarkReport` gains `DiffSection` automatically

**Effort:** M · 3–5 days

---

### I-03 · CLI Improvements

**Why:** The ConsoleApp is a demo; a proper CLI makes Delibera accessible to non-C# users.

**What:**
- `System.CommandLine`-based CLI with subcommands: `run`, `resume`, `compare`, `benchmark`
- `delibera run --config debate.json` — load configuration from file
- `delibera resume --id <debate-id>` — resume from checkpoint
- `delibera benchmark --configs dir/` — run comparison
- Output format flags: `--markdown`, `--html`, `--json`

**Effort:** M · 3–5 days

---

## 🔷 v10.5.0 — Scale & Reliability

> _Theme: Production-grade throughput and cost control_

### S-02 · Rate-Limiting & Cost Gates

**Why:** Cloud LLM calls cost real money. Users need guardrails before launching expensive 5-round multi-model debates.

**What:**
- `CouncilBuilder.WithCostLimit(decimal maxUsd)` — estimate cost before execution, abort if over limit
- `CouncilBuilder.WithRateLimit(int requestsPerMinute)` — throttle LLM calls per provider
- `DebateResult.CostEstimate` — token counts × model pricing (built-in pricing table, extensible)
- `CostLimitExceededException` — clear exception with partial results attached

**Effort:** M · 3–5 days

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
| 10.4.0 | Q4 2026 | 🔷 Planned |
| 10.5.0 | Q1 2027 | 🔷 Planned |
| 11.0.0 | .NET 11 GA | 🔶 Future |

---

*Delibera Roadmap · July 2026 · [techbuzzz/Delibera](https://github.com/techbuzzz/Delibera)*