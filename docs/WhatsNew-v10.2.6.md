# Delibera v10.2.6 — What's New

> **Status:** ✅ Shipped — July 2026  
> **9 of 10 planned features delivered** (F-06 Multi-Modal deferred to v10.2.7 per user request).  
> **259 unit tests pass** (up from 105 in v10.2.5).  
> **No breaking changes** — every new feature is opt-in via additional builder methods.

This document is the user-facing guide for the nine new capabilities in Delibera v10.2.6.
For the technical roadmap, see [docs/v10.2.6.md](v10.2.6.md). For the full changelog,
see [CHANGELOG.md](../CHANGELOG.md).

---

## Table of contents

- [F-08 — OpenTelemetry-style Observability](#f-08--opentelemetry-style-observability)
- [F-10 — Quick Wins Bundle](#f-10--quick-wins-bundle)
- [F-07 — Debate Templates & Presets](#f-07--debate-templates--presets)
- [F-01 — Async Streaming Council](#f-01--async-streaming-council)
- [F-09 — Dynamic Strategy Switching](#f-09--dynamic-strategy-switching)
- [F-02 — Pluggable Vote Engine](#f-02--pluggable-vote-engine)
- [F-05 — Structured Output / JSON Schema](#f-05--structured-output--json-schema)
- [F-03 — Debate Persistence & Resume](#f-03--debate-persistence--resume)
- [F-04 — Agent Memory](#f-04--agent-memory)
- [ConsoleApp demo entries](#consoleapp-demo-entries)

---

## F-08 — OpenTelemetry-style Observability

Production-grade observability for every debate — spans for the round loop,
metrics for duration, tokens, and compression ratio, with zero overhead
when no listener is attached.

```csharp
var executor = new CouncilBuilder()
    .AddMember("llama3.2", llm, "Analyst")
    .AddMember("qwen2.5", llm, "Critic")
    .WithStandardDebate()
    .WithUserPrompt("Microservices vs monolith?")
    .WithMaxRounds(3)
    .WithTelemetry()  // ← F-08
    .Build();

await executor.ExecuteAsync();

// Wire into your OpenTelemetry pipeline:
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("Delibera.Council").AddJaegerExporter())
    .WithMetrics(b => b.AddMeter("Delibera.Metrics").AddPrometheusExporter());
```

**Hierarchy emitted:**

```
delibera.council.execute
├── delibera.council.round          (per round, tag: round_number)
│   ├── delibera.member.respond     (per participant, tag: member_name)
│   ├── delibera.rag.query          (if Knowledge Keeper attached)
│   ├── delibera.compression        (if compression enabled)
│   └── delibera.operator.execute   (per Operator task)
└── delibera.chairman.synthesize
```

**Metrics emitted:**
- `delibera.debate.duration` (Histogram, ms)
- `delibera.round.duration` (Histogram, ms, tag: `round_number`)
- `delibera.tokens.total` (Counter, tags: `member_name`, `direction`)
- `delibera.compression.ratio` (Gauge)
- `delibera.debates.completed` (Counter, tags: `strategy`, `success`)

Configuration via `appsettings.json` `Delibera:Telemetry` section or
`WithTelemetry(Action<TelemetryOptions>)` delegate.

---

## F-10 — Quick Wins Bundle

Five small, high-impact features that improve DX and operability.

### F-10a — HTML export

```csharp
// Self-contained HTML with inline CSS, collapsible <details> rounds
var html = result.ToHtml(new HtmlExportOptions
{
    Theme = HtmlTheme.Dark,        // or Light
    CollapsibleRounds = true
});
await result.SaveToHtmlAsync("./result.html");
```

### F-10b — Debate timeout

```csharp
.WithTimeout(TimeSpan.FromMinutes(10))  // total wall-clock budget
```

The timeout is linked to the caller's `CancellationToken` via
`CancellationTokenSource.CreateLinkedTokenSource` and disposes both sources
in the `finally` block.

### F-10c — Persona presets

```csharp
.AddMember("llama3.2", llm, "Devil's Advocate", Persona.DevilsAdvocate)
.AddMember("qwen2.5", llm, "Risk Manager",     Persona.RiskManager)
.AddMember("mistral", llm, "Pragmatist",       Persona.Pragmatist)
```

Six built-in presets: `Expert`, `DevilsAdvocate`, `CautiousOptimist`,
`DataDrivenAnalyst`, `RiskManager`, `Pragmatist`. Resolved by
`Persona.Resolve(name)`.

### F-10d — Benchmark / model comparison

```csharp
var benchmark = new CouncilBenchmark()
    .AddConfiguration("Small", b => b.AddMember("llama3.2:1b", llm, "A"))
    .AddConfiguration("Standard", b => b.AddMember("llama3.2:3b", llm, "A"))
    .WithQuestion("Microservices or monolith?")
    .WithMaxRounds(3);

var report = await benchmark.RunAsync();
await report.SaveComparisonAsync("./benchmark.md");
```

Runs configs sequentially (deterministic, no rate-limit skew), records
failures per-entry, renders a side-by-side comparison with verdicts, token
usage, and latency tables.

### F-10e — Participant limit

```csharp
.WithParticipantLimit(maxParticipants: 5)  // throws on Build() if exceeded
```

Safety guard for dynamic DI-driven setups where the participant list is
built at runtime.

---

## F-07 — Debate Templates & Presets

Six ready-to-run council configurations for common use cases.

```csharp
var executor = DebateTemplate.ArchitectureReview
    .WithProvider(llm)
    .WithQuestion("Should we adopt event-driven architecture?")
    .WithMaxRounds(4)
    .Build();
```

**Available templates:**

| Template | Participants | Strategy | Use Case |
| --- | --- | --- | --- |
| `ArchitectureReview` | Architect, SecurityExpert, PerfEngineer | CritiqueDebate | System design |
| `RiskAssessment` | Optimist, Pessimist, Realist, RiskManager | ConsensusDebate | Business risks |
| `CodeReview` | Reviewer, Defender, QA, TechLead | CritiqueDebate | PR analysis |
| `ProductDecision` | PM, TechLead, UXDesigner | StandardDebate | Feature prioritisation |
| `SecurityAudit` | RedTeam, BlueTeam, Auditor | CritiqueDebate | Threat modelling |
| `DataArchitecture` | DataEngineer, DBA, MLEngineer | ConsensusDebate | Data platform |

`DebateTemplate.Custom()` returns a fresh `CouncilBuilder` for full control.
Templates can be refined with the same fluent API as `ICouncilBuilder`
(`WithMaxRounds`, `WithTemperature`, `AddMember`, `Advanced(...)`).

---

## F-01 — Async Streaming Council

Yield each `DebateRound` live as it completes — perfect for ASP.NET Core
SSE, WebSocket, Blazor, and CLI live output.

```csharp
// CLI live output
await foreach (var round in executor.StreamDebateAsync(ct))
{
    Console.WriteLine($"[Round {round.RoundNumber}/{round.Total}] {round.RoundName}");
    foreach (var (member, response) in round.Responses)
        Console.WriteLine($"  {member}: {Truncate(response, 200)}");
}

// ASP.NET Core SSE
app.MapGet("/debate/stream", async (HttpContext ctx, ICouncilExecutor executor) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    await foreach (var round in executor.StreamDebateAsync(ctx.RequestAborted))
    {
        var json = JsonSerializer.Serialize(round);
        await ctx.Response.WriteAsync($"data: {json}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
});
```

`DebateRound` gains `Total` (int?) and `IsFinal` (bool) for progress UIs.
`ICouncilExecutor.LastStreamedResult` exposes the aggregated `DebateResult`
(including logs and token stats) after the stream completes. `ExecuteAsync`
stays fully backward-compatible.

---

## F-09 — Dynamic Strategy Switching

Swap the debate strategy mid-flight when the discussion stagnates.

```csharp
var selector = new AdaptiveStrategySelector
{
    Initial = new StandardDebate(),
    OnStalemate = new CritiqueDebate(),
    StagnationThreshold = 2,        // consecutive low-diversity rounds
    StagnationScore = 0.3          // diversity cutoff (0.0-1.0)
};

var executor = new CouncilBuilder()
    .AddMember(/*...*/)
    .WithAdaptiveStrategy(selector)  // ← F-09
    .Build();
```

When no embedding provider is available, the selector falls back to
Levenshtein text-similarity. `DebateRound.StrategyUsed` records which
strategy produced each round for audit. Custom selectors can be implemented
via `IStrategySelector` for stalemate heuristics based on token usage, error
rates, or external signals.

> **Note:** Full mid-flight strategy swap requires strategies to be
> round-by-round abortable (future enhancement). The current implementation
> records the switch decision in logs and stamps `StrategyUsed` on the
> result; the switch itself happens at strategy boundaries.

---

## F-02 — Pluggable Vote Engine

Replace the single-LLM Chairman synthesis with a structured tally among
participants — a verifiable decision trail for compliance, risk committees,
and architecture boards.

```csharp
var votingStrategy = new WeightedVotingStrategy
{
    MemberWeights = { ["SecurityExpert"] = 2.0, ["Architect"] = 1.5 }
};

var executor = new CouncilBuilder()
    .AddMember("architect",       llm, "Architect")
    .AddMember("security-expert", llm, "SecurityExpert")
    .AddMember("perf-engineer",   llm, "PerfEngineer")
    .WithVotingChairman("qwen2.5", llm, votingStrategy)  // ← F-02
    .WithUserPrompt("Should we adopt microservices?")
    .WithMaxRounds(2)
    .Build();
```

**Built-in strategies:**

| Strategy | Description |
| --- | --- |
| `MajorityVotingStrategy` | Top-ranked option gets 1 point per ballot |
| `BordaCountVotingStrategy` | N-1 points for top, N-2 for second, ..., 0 for last |
| `WeightedVotingStrategy` | Per-member `MemberWeights` overrides |

After the debate, the executor asks each member to rank the options
surfaced in the final round, tallies the ballots, and renders a
🗳️ **Voting Tally** section in the Markdown output:

```markdown
## 🗳️ Voting Tally
**Method:** Weighted
**Winning option:** Microservices (score: 3.50)

| Option | Score |
|--------|------:|
| Microservices | 3.50 |
| Monolith | 1.50 |
```

Implement `IVotingStrategy` for custom methods (Condorcet, STV, Borda with
truncated rankings, etc.).

---

## F-05 — Structured Output / JSON Schema

Receive a strongly-typed verdict from the Chairman — no parsing free text.

```csharp
public sealed record ArchitectureDecision(
    string Recommendation,
    double Confidence,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Benefits,
    string Rationale);

var executor = new CouncilBuilder()
    .AddMember(/*...*/)
    .SetChairman("qwen2.5", llm)
    .WithStandardDebate()
    .WithUserPrompt("Migrate to microservices?")
    .WithMaxRounds(2)
    .WithStructuredOutput<ArchitectureDecision>()  // ← F-05
    .Build();

var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();
// verdict is a typed ArchitectureDecision, not a string to parse
```

A JSON schema is generated from the C# type via the .NET 10
`JsonSchemaExporter`, appended to the Chairman's synthesis prompt, and the
response is deserialised. One automatic retry with a correction prompt is
performed on deserialisation failure. Implement
`IStructuredOutputSerializer` for custom format (YAML, XML, etc.) or to
plug in `Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<T>()` for
providers that support native JSON-schema-constrained decoding.

---

## F-03 — Debate Persistence & Resume

Survive crashes, restarts, and intentional pauses by checkpointing the
debate after every round.

```csharp
var store = new FileDebateStore("./checkpoints", retentionDays: 30);

var executor = new CouncilBuilder()
    .AddMember(/*...*/)
    .WithUserPrompt("Long-running analysis question?")
    .WithMaxRounds(8)
    .WithPersistence(store)         // ← F-03: save checkpoint after every round
    .Build();

await executor.ExecuteAsync(ct);  // crashes mid-debate? No problem.

// Later, in a new process:
var resumed = new CouncilBuilder()
    .AddMember(/*...*/)
    .WithUserPrompt("Long-running analysis question?")
    .WithMaxRounds(8)
    .WithPersistence(store)
    .ResumeFrom("debate-2026-07-06-abc123")  // ← F-03
    .Build();

await resumed.ExecuteAsync(ct);
```

**`FileDebateStore`** uses atomic JSON write-then-rename so a crash
mid-write cannot leave a corrupt checkpoint. `InMemoryDebateStore` is
provided for tests. Each checkpoint carries a `DebateCheckpoint` snapshot
(DebateId, CreatedAt, LastCompletedRound, CompletedRounds, Options
snapshot, OriginalQuestion). DebateIds are ULID-style 26-char
lexicographically-sortable identifiers.

Configure retention via `appsettings.json` `Delibera:Persistence` section
(Enabled, Store, Directory, RetentionDays).

---

## F-04 — Agent Memory

Council members recall context from previous sessions and persist their
conclusions after each debate.

```csharp
var memory = new InMemoryAgentMemory();
// Or: new QdrantAgentMemory(ragProvider, embeddingProvider);
// Or: new PgVectorAgentMemory(ragProvider, embeddingProvider);

var executor = new CouncilBuilder()
    .AddMember("llama3.2", llm, "Architect")
    .AddMember("qwen2.5", llm, "Pragmatist")
    .WithStandardDebate()
    .WithUserPrompt("Should we adopt event-driven architecture?")
    .WithMaxRounds(2)
    .WithAgentMemory(memory)  // ← F-04
    .Build();

await executor.ExecuteAsync();  // members recall from previous sessions
await executor.ExecuteAsync();  // and persist new conclusions for next time
```

**Pre-execution** the executor recalls each member's top-3 memories (deduped
by content) and prepends a `Memory from previous sessions` block to the
system prompt. **Post-execution** it stores each member's last response and
the Chairman's verdict as a `MemoryEntry` record tagged with the member
display name and (when persistence is enabled) the debate id.

`InMemoryAgentMemory` uses Jaccard token-overlap similarity. The Qdrant and
PgVector backends use the existing `IRagProvider` for storage and
`IEmbeddingProvider` for semantic similarity — isolating per agent via
per-agent collections (Qdrant) or metadata filtering (pgvector).

---

## ConsoleApp demo entries

Each new feature has a `--flag` entry in the `Delibera.ConsoleApp`
demo menu (discoverable via `dotnet run` with no arguments):

| Flag | Example | Description |
| --- | --- | --- |
| `--telemetry` | `TelemetryExample` | In-process `ActivityListener` + `MeterListener` printing every span and metric. |
| `--quick-wins` | `QuickWinsExample` | HTML export, timeout, personas, benchmark, participant limit. |
| `--templates` | `TemplatesExample` | `DebateTemplate.ArchitectureReview` against a live Ollama. |
| `--stream` | `StreamingCouncilExample` | `IAsyncEnumerable<DebateRound>` live output. |
| `--adaptive-strategy` | `AdaptiveStrategyExample` | `StandardDebate` → `CritiqueDebate` switch on stagnation. |
| `--voting` | `VotingExample` | `WeightedVotingStrategy` with per-member weights. |
| `--structured-output` | `StructuredOutputExample` | `ArchitectureDecision` typed verdict. |
| `--persistence` | `PersistenceExample` | `FileDebateStore` with retention + auto-resume on existing checkpoint. |
| `--agent-memory` | `AgentMemoryExample` | `InMemoryAgentMemory` across successive debates. |

---

## Compatibility

- **No breaking changes.** Every new feature is opt-in via additional
  builder methods (`WithTelemetry`, `WithTimeout`, `WithVotingChairman`,
  `WithStructuredOutput<T>`, `WithPersistence`, `WithAgentMemory`,
  `WithAdaptiveStrategy`). The fluent API is fully backward compatible.
- Existing `ICouncilExecutor` implementations continue to work — the new
  `StreamDebateAsync` and `ExecuteTypedAsync<TVerdict>` are DIMs with
  default implementations that compose with the existing `ExecuteAsync`.

## Test summary

- 9 new test files, 163 new tests, **all 259 tests pass** (up from 105 in
  v10.2.5).
- Coverage includes: serializer roundtrip, schema generation, streaming
  cancellation, adaptive stalemate detection, voting tally math,
  structured output retry logic, checkpoint save/restore, agent memory
  per-agent isolation, and end-to-end execution paths.

## Migration guide

If you're upgrading from v10.2.5 to v10.2.6:

1. **Bump the package version** in your `csproj` to `10.2.6`.
2. **No code changes required.** All new features are opt-in.
3. **To opt into any new feature**, call the corresponding `With*` method on
   `CouncilBuilder` — e.g. `.WithTelemetry()`, `.WithTimeout(...)`,
   `.WithVotingChairman(...)`, etc.
4. **To consume new surfaces** (e.g. `StreamDebateAsync`, `AgentMemory`),
   cast your executor to `CouncilExecutor` or use the `ICouncilExecutor`
   interface — both expose the new properties.

## See also

- [CHANGELOG.md](../CHANGELOG.md) — full version history
- [docs/v10.2.6.md](v10.2.6.md) — original technical roadmap
- [docs/QuickStart.md](QuickStart.md) / [docs/QuickStart-RU.md](QuickStart-RU.md) — quick start
- [docs/NET10-Upgrade.md](NET10-Upgrade.md) — .NET 10 upgrade notes
- [docs/ChatClientLLMProvider.md](ChatClientLLMProvider.md) — Microsoft.Extensions.AI bridge
