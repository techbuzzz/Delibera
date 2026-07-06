# 🗺️ Delibera — Multi-Version Roadmap

> **Current stable:** `10.2.4`  
> **Active branch:** `develop`  
> **Document date:** July 2026  
> **Versioning:** [Semantic Versioning 2.0](https://semver.org) · MINOR = new feature, PATCH = fix/polish

---

## 📋 Version Overview

| Version | Theme | Key Deliverables | Status |
|---------|-------|-----------------|--------|
| **10.3.0** | _Streaming & Voting_ | Async Streaming Council, Pluggable Vote Engine | 🔵 In Progress |
| **10.4.0** | _Resilient Storage_ | Debate Persistence & Resume, HTML Export | 🔷 Planned |
| **10.5.0** | _Intelligence Layer_ | Agent Memory, Structured Output / JSON Schema | 🔷 Planned |
| **10.6.0** | _Multi-Modal_ | Vision + Document Attachments | 🔷 Planned |
| **10.7.0** | _Observability & DX_ | OpenTelemetry, Templates Library, Quick Wins | 🔷 Planned |
| **10.8.0** | _Adaptive Orchestration_ | Dynamic Strategy Switching, Benchmark Mode | 🔷 Planned |
| **11.0.0** | _Platform Release_ | Breaking-change cleanup, ASP.NET Core Server, gRPC API, NuGet GA | 🔶 Future |

---

## 🔵 v10.3.0 — Streaming & Voting

> _Theme: Real-time feedback + auditable decisions_

### F-01 · Async Streaming Council

**Why:** `ExecuteAsync()` currently blocks for the full debate duration.  
For long debates (4+ rounds, large models) users get zero feedback — no progress, no early results.

**What:**
- New `IAsyncEnumerable<DebateRound> StreamDebateAsync(CancellationToken)` on `ICouncilExecutor`
- Each round yielded as it completes, including Chairman's final synthesis round
- `DebateRound` gains: `int Total`, `TimeSpan Duration`, `bool IsFinal`
- `ExecuteAsync()` stays **100% backward-compatible** (internally collects the stream)
- New `--stream` ConsoleApp demo with live round-by-round output

**Unlocks:** ASP.NET Core SSE endpoints, Blazor real-time UI, WebSocket push, CLI progress bars.

```csharp
await foreach (var round in executor.StreamDebateAsync(ct))
    Console.WriteLine($"[Round {round.Number}/{round.Total}] {round.Summary}");
```

**Files:**
- `Delibera.Core/Council/ICouncilExecutor.cs` — new default method
- `Delibera.Core/Council/CouncilExecutor.cs` — `yield return` refactor of round loop
- `Delibera.Core/Models/DebateRound.cs` — new fields
- `Delibera.ConsoleApp/Examples/StreamingExample.cs`

**Effort:** M · 3–5 days  
**Tests:** streaming cancellation, round ordering, backward-compat `ExecuteAsync` parity

---

### F-02 · Pluggable Vote / Consensus Engine

**Why:** Enterprise scenarios (compliance boards, architecture committees) need a **verifiable tally**,  
not just Chairman's free-text synthesis. Decision trail must be auditable.

**What:**
- New `IVotingStrategy` + built-in implementations: `MajorityVoting`, `BordaCount`, `WeightedVoting`
- `Chairman.CreateVoting(IVotingStrategy)` factory alongside `CreateStandard`
- `DebateResult.VotingTally` optional property
- **🗳️ Voting Tally** section rendered in `*_result.md`

```csharp
.WithVotingChairman(new WeightedVotingStrategy
{
    MemberWeights = new() { ["SecurityExpert"] = 2.0, ["Architect"] = 1.5 }
})
```

**Files:**
- `Delibera.Core/Voting/` — new folder
- `Delibera.Core/Council/Chairman.cs` — `CreateVoting` factory
- `Delibera.Core/Models/DebateResult.cs` — `VotingTally`

**Effort:** M · 3–5 days  
**Tests:** per-strategy tally with known inputs, weights validation, fallback to synthesis

---

## 🔷 v10.4.0 — Resilient Storage

> _Theme: Reliability for production-grade long debates_

### F-03 · Debate Persistence & Resume

**Why:** A crash at round 3 of a 5-round debate with expensive cloud models wastes money and time.  
Persistence + resume is table-stakes for production use.

**What:**
- New `IDebateStore` interface with `SaveCheckpointAsync` / `LoadCheckpointAsync` / `ListAsync` / `DeleteAsync`
- `FileDebateStore` (atomic JSON write-then-rename), `InMemoryDebateStore` (testing)
- `DebateResult.DebateId` — auto-generated ULID
- `CouncilBuilder.WithPersistence(IDebateStore)` + `.ResumeFrom(debateId)`
- `Delibera:Persistence` configuration section in `appsettings.json`

```csharp
.WithPersistence(new FileDebateStore("./checkpoints"))
.ResumeFrom("debate-2026-07-06-abc123")
```

**Files:**
- `Delibera.Core/Persistence/` — new folder
- `Delibera.Core/Council/CouncilExecutor.cs` — checkpoint after each round
- `Delibera.Core/Models/DebateResult.cs` — `DebateId`

**Effort:** L · 5–8 days  
**Tests:** crash-and-resume integration test, atomic write, ULID generation, CT propagation

---

### F-10a · HTML Export (`DebateResult.ToHtml`)

**Why:** Markdown is great for developers; business stakeholders want a styled, shareable HTML report.

**What:**
- `DebateResult.SaveToHtmlAsync(path, HtmlExportOptions?, CancellationToken)` 
- `HtmlExportOptions` — `Theme` (Light / Dark), `CollapsibleRounds` (bool), `EmbedCss` (bool)
- Self-contained single-file HTML (no CDN dependencies, embedded CSS)

```csharp
await result.SaveToHtmlAsync("report.html", new HtmlExportOptions { Theme = HtmlTheme.Dark });
```

**Files:**
- `Delibera.Core/Output/HtmlExporter.cs` — new
- `Delibera.Core/Output/Templates/report.html.template` — embedded resource

**Effort:** S · 1 day  

---

### F-10b · `CouncilBuilder.WithTimeout(TimeSpan)`

**Why:** Callers shouldn't have to manage `CancellationTokenSource` just to set a time limit.

**What:**
- One extension method on `CouncilBuilder` — wraps `CancellationTokenSource` internally,  
  linked to the caller's token on `ExecuteAsync`

```csharp
.WithTimeout(TimeSpan.FromMinutes(10))
```

**Effort:** XS · 2 hours  

---

## 🔷 v10.5.0 — Intelligence Layer

> _Theme: Memory + type-safe outputs — closing the gap to autonomous agents_

### F-04 · Agent Memory & Long-Term Context

**Why:** Repeated consultations on evolving topics (ongoing architecture decisions, legal analysis)  
lose context between sessions. Agents should *remember*.

**What:**
- New `IAgentMemory` interface — `StoreAsync` / `RecallAsync`
- Built on existing `IRagProvider`: `QdrantAgentMemory`, `PgVectorAgentMemory`, `InMemoryAgentMemory`
- `CouncilMember.Memory` optional property
- Recalled memories injected as clearly-labelled context block `[Memory from previous sessions]`
- Memories stored automatically after each debate conclusion

```csharp
.WithAgentMemory(new QdrantAgentMemory(qdrantConfig))
```

**Files:**
- `Delibera.Core/Memory/` — new folder  
- `Delibera.Core/Council/CouncilMember.cs` — `Memory` property
- `Delibera.Core/Council/CouncilExecutor.cs` — recall before round, store after verdict

**Effort:** L · 5–8 days  
**Tests:** store → recall across two debates, CT propagation, `InMemoryAgentMemory` default

---

### F-05 · Structured Output / JSON Schema Enforcement

**Why:** Downstream CI/CD pipelines, approval systems, and dashboards need **machine-readable verdicts**,  
not free-text Markdown requiring fragile parsing.

**What:**
- `ICouncilExecutor.ExecuteTypedAsync<T>(CancellationToken)` — returns `DebateResult` with `T TypedVerdict`
- `CouncilBuilder.WithStructuredOutput<T>()` — fluent API
- Schema emitted via `System.Text.Json` source generators (zero reflection)
- One automatic retry with correction prompt on deserialization failure
- Markdown output preserved alongside JSON (unless `OutputFormat = JsonOnly`)

```csharp
var result = await executor.ExecuteTypedAsync<ArchitectureDecision>(ct);
ArchitectureDecision verdict = result.TypedVerdict;
```

**Files:**
- `Delibera.Core/Output/IStructuredOutputSerializer.cs` — new
- `Delibera.Core/Output/JsonSchemaOutputSerializer.cs` — new
- `Delibera.Core/Council/ICouncilExecutor.cs` — `ExecuteTypedAsync<T>`

**Effort:** M · 3–5 days  
**Tests:** valid schema response, malformed JSON retry, type mismatch, `JsonOnly` mode

---

### F-10c · Participant Persona Presets

**Why:** Users shouldn't write system prompts from scratch for common debate roles.

**What:**
- `Persona` static class with built-in prompt templates
- `AddMember(model, provider, Persona.DevilsAdvocate)` overload
- Built-in: `Expert`, `DevilsAdvocate`, `CautiousOptimist`, `DataDrivenAnalyst`, `RiskManager`, `Pragmatist`
- Fully additive — `string systemPrompt` overload unchanged

```csharp
.AddMember("llama3.2:3b", ollama, Persona.DevilsAdvocate)
.AddMember("qwen2.5:7b",  ollama, Persona.DataDrivenAnalyst)
```

**Files:** `Delibera.Core/Council/Persona.cs` — new  
**Effort:** XS · 2 hours  

---

## 🔷 v10.6.0 — Multi-Modal

> _Theme: Bring images, diagrams, and documents into the debate_

### F-06 · Multi-Modal Council (Vision + Documents)

**Why:** Real architecture decisions involve visual artifacts — C4 diagrams, ERDs, wireframes, PDFs.  
Vision-capable models (`llava`, `gemma3`, `gpt-4o`) should participate when given image context.

**What:**
- `MemberCapabilities` enum: `Text = 1`, `Vision = 2` (auto-detected via capability registry)
- `DebateAttachment` hierarchy: `ImageAttachment`, `PdfAttachment`, `TextAttachment`
- `CouncilBuilder.WithAttachment(DebateAttachment)` — fluent API
- Image attachments → base64 `ImageContent` via `Microsoft.Extensions.AI` `ChatMessage` (already integrated)
- PDF extraction → plain text, chunked via existing AutoChunking if large
- Non-vision members receive: `[Image: description — not available for this model]`
- `ModelCapabilitiesRegistry` — auto-detect vision support by model name (extends existing `ModelContextWindowRegistry`)

```csharp
.AddMember("llava:13b", ollama, "Visual Analyst",
    capabilities: MemberCapabilities.Vision | MemberCapabilities.Text)
.WithAttachment(new ImageAttachment("./arch-diagram.png", "System Architecture"))
.WithAttachment(new PdfAttachment("./requirements.pdf"))
```

**Files:**
- `Delibera.Core/Attachments/` — new folder
- `Delibera.Core/Council/CouncilMember.cs` — `Capabilities` property
- `Delibera.Core/Council/CouncilExecutor.cs` — route attachments per capability
- `Delibera.Core/Chunking/ModelCapabilitiesRegistry.cs` — extend existing registry

**NuGet:** `PdfPig` (or `iText Community`) for PDF text extraction — optional dependency

**Effort:** L · 7–10 days  
**Tests:** vision routing, PDF extraction + chunking, capability auto-detection, non-vision fallback text

---

## 🔷 v10.7.0 — Observability & DX

> _Theme: Production-ready monitoring + developer happiness_

### F-08 · OpenTelemetry Observability

**Why:** Existing `ExecutionLog` is great for file output but not integrated with standard .NET observability.  
Production deployments need distributed traces, latency histograms, and token-usage metrics.

**What:**

```
delbera.council.execute
├── delibera.council.round        (tags: round_number, strategy)
│   ├── delibera.member.respond   (tags: member_name, model, tokens_in, tokens_out)
│   ├── delibera.rag.query        (tags: provider, results_count)
│   ├── delibera.compression      (tags: strategy, ratio)
│   └── delibera.operator.execute (tags: tool_name)
└── delibera.chairman.synthesize  (tags: model, tokens_total)
```

**Metrics:**

| Metric | Type | Tags |
|--------|------|------|
| `delibera.debate.duration` | Histogram (ms) | — |
| `delibera.round.duration` | Histogram (ms) | `round_number` |
| `delibera.tokens.total` | Counter | `member_name`, `direction` |
| `delibera.compression.ratio` | Gauge | `strategy` |
| `delibera.rag.results_count` | Histogram | `provider` |

```csharp
services.AddDelibera(o => o.Telemetry.Enabled = true);
// Standard .NET OTEL — user's responsibility
services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("Delibera.Council").AddJaegerExporter())
    .WithMetrics(b => b.AddMeter("Delibera.Metrics").AddPrometheusExporter());
```

**Files:** `Delibera.Core/Telemetry/` — `DeliberaActivitySource`, `DeliberaMeter`, `TelemetryOptions`  
**No new deps** — uses in-box `System.Diagnostics.Activity`, zero overhead when no listener attached  
**Effort:** M · 3–5 days

---

### F-07 · Debate Templates & Presets Library

**Why:** Reducing boilerplate for common scenarios improves onboarding dramatically.

**Included templates:**

| Template | Participants | Strategy |
|----------|-------------|----------|
| `ArchitectureReview` | Architect, SecurityExpert, PerfEngineer | CritiqueDebate |
| `RiskAssessment` | Optimist, Pessimist, Realist, RiskManager | ConsensusDebate |
| `CodeReview` | Reviewer, Defender, QA, TechLead | CritiqueDebate |
| `ProductDecision` | PM, TechLead, UXDesigner | StandardDebate |
| `SecurityAudit` | RedTeam, BlueTeam, Auditor | CritiqueDebate |
| `DataArchitecture` | DataEngineer, DBA, MLEngineer | ConsensusDebate |

```csharp
var executor = DebateTemplate.ArchitectureReview
    .WithQuestion("Should we migrate to event-driven architecture?")
    .WithProvider(ollama)
    .WithMaxRounds(4)
    .Build();
```

**Files:** `Delibera.Core/Templates/` — `DebateTemplate` static class, one file per template  
**Effort:** S · 1–2 days

---

### F-10d · Benchmark / Model Comparison Mode

**Why:** Users need to compare model quality and cost tradeoffs before committing to a configuration.

**What:**
```csharp
var benchmark = new CouncilBenchmark()
    .AddConfiguration("Small",    b => b.AddMember("llama3.2:1b", ...) ...)
    .AddConfiguration("Standard", b => b.AddMember("llama3.2:3b", ...) ...)
    .WithQuestion("Microservices vs Monolith?")
    .WithMaxRounds(3);

var report = await benchmark.RunAsync();
await report.SaveComparisonAsync("./benchmark.md");
// → side-by-side verdicts, token usage, latency, cost estimate per config
```

**Files:** `Delibera.Core/Benchmarking/CouncilBenchmark.cs`, `BenchmarkReport.cs`  
**Effort:** S · 1–2 days

---

## 🔷 v10.8.0 — Adaptive Orchestration

> _Theme: Self-improving debates — the council learns as it runs_

### F-09 · Dynamic Strategy Switching

**Why:** StandardDebate can fall into agreement loops; CritiqueDebate can become unproductive.  
An adaptive orchestrator detects stagnation and escalates/de-escalates the debate mode.

**What:**
- New `IStrategySelector` interface with `ValueTask<IDebateStrategy?> SelectNextAsync(DebateProgress, CancellationToken)`
- `DebateProgress` record: `CurrentRound`, `MaxRounds`, `ResponseDiversityScore`, `IsStalemate`
- `ResponseDiversityScore` — cosine similarity via existing `IEmbeddingProvider` (falls back to 0)
- Built-in: `AdaptiveStrategySelector` (stalemate → escalate), `EscalatingStrategySelector`
- `DebateRound.StrategyUsed` — audit trail
- Strategy switches logged in `ExecutionLog` and rendered in Markdown

```csharp
.WithAdaptiveStrategy(new AdaptiveStrategySelector
{
    Initial = new StandardDebate(),
    OnStalemate = new CritiqueDebate(),
    StagnationThreshold = 2
})
```

**Files:**
- `Delibera.Core/Debate/IStrategySelector.cs` — new
- `Delibera.Core/Debate/AdaptiveStrategySelector.cs` — new
- `Delibera.Core/Models/DebateProgress.cs` — new
- `Delibera.Core/Council/CouncilExecutor.cs` — call selector after each round

**Effort:** M · 3–5 days  
**Tests:** stalemate detection triggers switch, diverse responses keep strategy, no selector = existing behavior

---

### Cross-Cutting v10.8 Additions

- **`CouncilBuilder.WithParticipantLimit(int)`** — guard misconfiguration in DI-driven setups
- **`DebateResult.QualityScore`** — heuristic 0–1 score: response diversity + round convergence + Chairman confidence keywords
- **`IDebateStrategy.GetProgressSummary()`** — optional interface method for mid-debate summaries (e.g. "Council has converged on option A after round 2")

---

## 🔶 v11.0.0 — Platform Release

> _Theme: Delibera as a platform, not just a library_  
> _This is a **major version** — breaking changes are intentional and documented_

### Breaking-Change Cleanup

Items deferred from previous versions to maintain backward compatibility:

- Remove all `[Obsolete]` members accumulated since v10.1
- Consolidate `CouncilBuilder` method overloads (reduce API surface)
- `ILLMProvider` — make `GetModelCapabilitiesAsync` non-optional (remove default `null` return)
- Unify `IDebateStrategy.ExecuteAsync` signatures (remove `IDebateStrategyWithOptions` shim)
- Rename `RagProviderFactory` → `VectorStoreFactory` for clarity

### Delibera.Server — ASP.NET Core Hosted Service

New NuGet package: **`Delibera.Server`**

- REST API: `POST /api/debates` (start), `GET /api/debates/{id}` (status), `DELETE /api/debates/{id}` (cancel)
- SSE endpoint: `GET /api/debates/{id}/stream` — powered by F-01 Streaming Council
- WebSocket support for bidirectional debate interaction (inject questions mid-debate)
- Background queue via `IHostedService` + `System.Threading.Channels`
- OpenAPI/Swagger spec included
- Docker-ready: extends existing `docker-compose.yml`

```csharp
// Program.cs
builder.Services.AddDeliberaServer(options =>
{
    options.MaxConcurrentDebates = 5;
    options.RequireAuthentication = true;
});
app.MapDeliberaEndpoints();
```

### Delibera.Grpc — gRPC API

New NuGet package: **`Delibera.Grpc`**

- `.proto` schema for streaming debate execution
- Bi-directional streaming: client sends config, server streams rounds back
- Auto-generated C# client + server stubs
- Suitable for high-throughput internal microservice integration

### NuGet GA Milestone

- Stable `11.0.0` on NuGet (exit preview)
- `Delibera.Core` — core library (no ASP.NET dep)
- `Delibera.Server` — ASP.NET Core hosted service
- `Delibera.Grpc` — gRPC interface
- `Delibera.Templates` — presets library (optional)
- Symbol packages + source link for all packages

---

## 📐 Architectural Principles (all versions)

### Backward Compatibility Policy

| Version range | Policy |
|---|---|
| `10.x.y` | No breaking changes. New params have `= default`. Obsolete via `[Obsolete]` with migration note. |
| `11.0.0` | Planned breaking changes only. Full migration guide published alongside release. |

### Definition of Done (every feature)

- [ ] Public API has XML doc comments
- [ ] `CancellationToken` propagated through all new async paths
- [ ] Unit tests ≥ 80% coverage of new code paths
- [ ] ConsoleApp demo with `--flag`
- [ ] `README.md` + `README-RU.md` updated
- [ ] `CHANGELOG.md` entry following Keep a Changelog format
- [ ] No breaking changes to `CouncilBuilder` fluent API (until v11.0)

### Branching Convention

```
develop                        ← integration branch
feature/f-01-streaming         ← one branch per feature
feature/f-02-voting
feature/f-03-persistence
release/10.3.0                 ← release stabilization branch
```

---

## 📦 Release Schedule (indicative)

| Version | Estimated | Notes |
|---------|-----------|-------|
| 10.3.0 | Q3 2026 | F-01 + F-02 |
| 10.4.0 | Q3 2026 | F-03 + F-10a + F-10b |
| 10.5.0 | Q4 2026 | F-04 + F-05 + F-10c |
| 10.6.0 | Q4 2026 | F-06 |
| 10.7.0 | Q1 2027 | F-07 + F-08 + F-10d |
| 10.8.0 | Q1 2027 | F-09 + cross-cutting polish |
| 11.0.0 | Q2 2027 | Platform release, breaking-change cleanup |

---

*Delibera Roadmap · July 2026 · [techbuzzz/Delibera](https://github.com/techbuzzz/Delibera)*
