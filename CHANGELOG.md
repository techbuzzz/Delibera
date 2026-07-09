# Changelog

All notable changes to **Delibera** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [10.3.0] - 2026

### ⚠️ Breaking Changes (P-01)

| Removed | Replacement |
|---------|-------------|
| `Moderator` static class | `Chairman` |
| `ICouncilBuilder.SetModerator(CouncilMember)` | `ICouncilBuilder.SetChairman(CouncilMember)` |
| `ICouncilBuilder.SetModerator(string, ILLMProvider, string?)` | `ICouncilBuilder.SetChairman(string, ILLMProvider, string?)` |
| `IDebateStrategyWithOptions` interface | `IDebateStrategy` (full signature with `DebateExecutionOptions`) |
| `IDebateStrategy.ExecuteAsync` overload without `DebateExecutionOptions` | Use the `DebateExecutionOptions` overload (pass `DebateExecutionOptions.Default`) |
| `ILLMProvider.GetModelCapabilitiesAsync` default `null` return | Implement explicitly; return `ModelCapabilities.Unknown(model)` when unknown |
| `ModelCapabilities?` (nullable) return type | `ModelCapabilities` (non-nullable); check `caps.IsUnknown` instead of `caps is null` |
| `RagProviderFactory` class | `VectorStoreFactory` |
| `IRagProviderFactory` interface | `IVectorStoreFactory` |

### Changed

- **`IDebateStrategy.ExecuteAsync`** now requires a `DebateExecutionOptions` parameter. The legacy overload without `DebateExecutionOptions` has been removed. Implement `IDebateStrategy` with the full signature; pass `DebateExecutionOptions.Default` when calling from code that doesn't need custom options.
- **`ILLMProvider.GetModelCapabilitiesAsync`** now returns `ModelCapabilities` (non-nullable) instead of `ModelCapabilities?`. Providers that cannot introspect capabilities should return `ModelCapabilities.Unknown(modelName)`. Callers should check `caps.IsUnknown` instead of `caps is null`.
- **`ModelCapabilities`** now has an `IsUnknown` property for checking whether the instance represents unknown capabilities.
- **`WeightedVotingStrategy.ResolveWeight`** now falls back to `ballot.Weight` when a member is not in `MemberWeights`, instead of the constructor's `defaultWeight`. This makes per-ballot weights work as documented.

### Added (S-01 — Distributed Debates)

- **`IDebateOrchestrator`** interface with `ExecuteAsync`, `EnqueueAsync`, `GetStatusAsync`, `StreamAsync`, `CancelAsync` — abstracts debate execution from the transport layer.
- **`LocalDebateOrchestrator`** — default in-process implementation wrapping `CouncilExecutor`, using `Channel<DebateRound>` for SSE streaming.
- **`RedisDebateOrchestrator`** — distributed implementation in `Delibera.Redis` project, publishing round events to Redis Streams, persisting state in Redis hashes.
- **`DebateHandle`**, **`DebateOrchestrationStatus`**, **`DebateRoundEvent`** — core types for the orchestration layer.
- **`DebateWorkerService`** — background service consuming jobs from Redis Streams.
- **`RedisOrchestratorOptions`** — configuration for Redis connection, stream keys, consumer groups.
- **`RedisOrchestratorExtensions.AddRedisDebateOrchestrator()`** — DI extension to swap `LocalDebateOrchestrator` for Redis.
- **`DebateOrchestrationService`** now delegates execution to `IDebateOrchestrator`, enabling local or distributed mode via DI.
- **`Delibera.Redis`** project (net10.0 class library) with `StackExchange.Redis 2.8.24` dependency.

### Added (S-03 — Result Caching)

- **`IDebateCache`** interface — `GetAsync`, `SetAsync`, `InvalidateAsync`, `ExistsAsync`.
- **`CacheBehavior`** enum — `Disabled`, `ReadWrite`, `ReadOnly`, `WriteThrough`, `Bypass`.
- **`DebateCacheKeyGenerator`** — deterministic SHA-256 cache key from debate inputs.
- **`InMemoryDebateCache`** — `IMemoryCache`-backed implementation with configurable TTL.
- **`FileDebateCache`** — JSON file-based implementation with TTL via file modification time.
- **`RedisDebateCache`** — `StackExchange.Redis`-backed implementation using `SETEX`.
- **`DebateResult.CacheHit`**, **`.CacheKey`**, **`.CachedAt`** — cache metadata on results.
- **`ICouncilBuilder.WithCacheBehavior(CacheBehavior)`** — per-debate cache control.
- **`ICouncilBuilder.WithCache(CacheBehavior, IDebateCache)`** — set both behavior and backend.
- **`CouncilExecutor`** now checks cache before execution and writes back on completion.
- DI extensions: **`UseInMemoryCache()`**, **`UseFileCache()`**, **`UseRedisCache()`**.
- **`debate.cache_hit`** OpenTelemetry counter metric emitted on cache hits.
- **`CacheHit`** / **`CacheKey`** fields added to `DebateResponse` DTO.

### Fixed

- **`WeightedVotingStrategy.ResolveWeight`** now uses `ballot.Weight` as fallback instead of constructor `defaultWeight`, fixing `WeightedVoting_All_Zero_Weights_Throws`.

## [10.2.7] - 2026

Multi-Modal Council (F-06): bring images, diagrams, and documents into the
debate with zero forced dependencies in `Delibera.Core`.

### Added — F-06 Multi-Modal Council (Vision + Documents)

- **`Delibera.Core/Attachments/`** — new namespace with:
  - **`IFileContentReader`** — single extensibility point for document parsing.
    The core package ships **no** PDF/DOCX libraries; users register a reader
    per extension via `WithFileReader`.
  - **`FileReadResult`** — normalised output record (`SourcePath`,
    `TextContent`, `BinaryParts`, `Metadata`). Text + binary can both be
    non-null (e.g. PDF with embedded images).
  - **`BinaryAttachment`** — raw binary part (image bytes) with MIME type,
    ready for vision models via `Microsoft.Extensions.AI` `ImageContent`.
  - **`FileAttachment`** — file-attachment record (`FilePath`, `Description`).
  - **`FileContentReaderRegistry`** — per-extension registry, pre-populated
    with built-in readers. Always returns a reader (fallback for unknown
    extensions — never throws).
  - **`FileReaderDIEntry`** — DI entry record for `AddFileReader` extension.

- **Built-in readers** (zero external deps in `Delibera.Core`):
  - **`PlainTextFileReader`** — `.txt .md .markdown .json .xml .cs .yml .yaml .csv .html .htm .log .tsv`
  - **`ImageFileReader`** — `.png .jpg .jpeg .webp .gif .bmp` → `BinaryParts` with correct `MediaType`
  - **`FallbackFileReader`** — any unregistered extension → graceful placeholder text
  - **`DelegateFileContentReader`** — lambda adapter (no class needed)

- **`MemberCapabilities`** flags enum (`Text`, `Vision`) added to
  `Delibera.Core.Models`. `CouncilMember` gains `Capabilities` property and
  `SupportsVision` computed property.

- **`ModelContextWindowRegistry`** extended with vision detection:
  - `SupportsVision(string)` — substring match against known vision patterns
    (`llava`, `gemma3`, `llama3.2-vision`, `minicpm-v`, `gpt-4o`,
    `gpt-4-vision`, `claude-3`, `qwen-vl`, `internvl`, `pixtral`, `llama4`, …)
  - `GetCapabilities(string)` — combines context-window + vision detection
  - `RegisterVisionPattern(string)` — add custom patterns at startup
  - `GetVisionPatterns()` — snapshot of all registered patterns

- **`CouncilBuilder`** fluent API:
  - `WithAttachment(string filePath)` — attach a file (read lazily on debate start)
  - `WithAttachment(string filePath, string description)` — attach with description
  - `WithFileReader(string extension, IFileContentReader reader)` — register instance
  - `WithFileReader(string extension, Func<string, CancellationToken, Task<FileReadResult>> handler)` — register lambda
  - `AddMember(string, ILLMProvider, string, MemberCapabilities, string?)` — explicit capabilities overload
  - `AddMember(string, ILLMProvider, string?, string?)` — now auto-detects vision from model name

- **`ICouncilExecutor`** gains `Attachments` (`IReadOnlyList<FileAttachment>`) and
  `FileReaders` (`FileContentReaderRegistry`) surfaces.

- **`CouncilExecutor.ExecuteCoreAsync`** reads attachments via the registry and
  injects text content into the debate context's `KnowledgeContent`. Binary parts
  are available for future `Microsoft.Extensions.AI` `ImageContent` routing (the
  text-injection path is the foundation; full vision-model routing is a future
  enhancement that requires per-member message construction).

- **DI registration**: `ServiceCollectionExtensions.AddFileReader(extension, factory)`
  registers a `FileReaderDIEntry` singleton so DI-resolved `CouncilBuilder`
  instances can pick up custom readers automatically.

### Changed

- Bumped `Delibera.Core` package version `10.2.6` → `10.2.7`.
- `CouncilMember.AddMember(string, ILLMProvider, string?, string?)` now
  auto-detects `MemberCapabilities.Vision` from the model name.
- `<Description>` and `<PackageTags>` updated to mention multi-modal support.

### Compatibility

- **No breaking changes.** The new `AddMember` overload with `MemberCapabilities`
  is additive; existing callers get auto-detection for free. `WithAttachment`
  and `WithFileReader` are opt-in.

### Test summary

- 1 new test file, 48 new tests, **all 307 tests pass** (up from 259 in v10.2.6).
- Coverage: PlainTextFileReader roundtrip, ImageFileReader binary parts + MIME,
  FallbackFileReader placeholder, DelegateFileContentReader lambda,
  FileContentReaderRegistry (pre-registered, register instance, register lambda,
  fallback for unknown), MemberCapabilities flags, vision auto-detection
  (10 vision + 5 text-only model names), GetCapabilities combination,
  RegisterVisionPattern, CouncilBuilder wiring (WithAttachment, WithFileReader,
  AddMember with capabilities, auto-detect), end-to-end execution with text
  attachment, fallback reader, and custom reader.

### ConsoleApp

- `MultiModalExample` (`--multimodal`) — demos vision member + text member +
  attachments + custom `.pdf` reader (lambda style).

## [10.2.6] - 2026

A large feature release that delivers nine new capabilities across
observability, DX, enterprise decision support, persistence, and memory. Every
new feature is backward-compatible — existing call sites continue to work
unchanged. The release also adds nine new `--flag` demo entries to the
ConsoleApp (Streaming, Templates, Quick Wins, Telemetry, Adaptive Strategy,
Voting, Structured Output, Persistence, Agent Memory).

### Added — F-08 OpenTelemetry-style observability

- **`Delibera.Core/Telemetry/`** — new namespace with:
  - **`TelemetryOptions`** — `Enabled`, `ActivitySourceName`, `MeterName`, `ServiceVersion`.
    Bound from `Delibera:Telemetry` configuration section.
  - **`DeliberaActivitySource`** — centralised `System.Diagnostics.ActivitySource` facade
    with `static readonly` fields. Returns `null` activities when no listener is
    attached (the standard .NET zero-overhead pattern).
  - **`DeliberaMeter`** — `System.Diagnostics.Metrics.Meter` with histograms
    (`delibera.debate.duration`, `delibera.round.duration`), counters
    (`delibera.tokens.total`, `delibera.debates.completed`), and gauge
    (`delibera.compression.ratio`).
  - **`DeliberaTelemetry`** — high-level facade (`StartDebate`, `StartRound`,
    `StartMemberRespond`, `StartRagQuery`, `StartCompression`,
    `StartOperatorTask`, `StartChairmanSynthesize`, `StartChairmanOpen`,
    `RecordDebateCompleted`, `RecordRoundDuration`, `RecordTokens`,
    `RecordCompressionRatio`, `MarkSucceeded`, `MarkFailed`).
  - **`DeliberaActivityNames`** / **`DeliberaTelemetryTags`** — canonical span
    names and tag keys (single source of truth).
- **`CouncilBuilder.WithTelemetry(TelemetryOptions?)`** + delegate overload
  `WithTelemetry(Action<TelemetryOptions>)`.
- **`ICouncilExecutor.IsTelemetryEnabled`** + `DebateTimeout` + `LastStreamedResult`
  + `StrategySelector` + `VotingStrategy` + `StructuredOutputSerializer` +
  `StructuredOutputType` + `DebateStore` + `ResumeFromDebateId` + `AgentMemory`
  surfaces.
- `CouncilExecutor.ExecuteAsync` instruments: top-level `delibera.council.execute`
  span + per-round duration histogram + aggregate token counter + compression
  ratio gauge + debate-completed counter. `CompressTextAsync` wraps every
  compression call in a `delibera.compression` span.
- `GetInfo()` prints a new `── Telemetry ──` section when enabled.
- 22 unit tests; 0 NuGet deps added in `Delibera.Core` (uses in-box APIs).

### Added — F-10 Quick Wins Bundle (HTML / Timeout / Personas / Benchmark / Limit)

- **F-10a HTML export** — `Output/HtmlExporter.cs` with `HtmlTheme` (Light/Dark),
  `HtmlExportOptions` (theme, collapsibility, inline-CSS, title). `DebateResult`
  gains `ToHtml(HtmlExportOptions?)` and `SaveToHtmlAsync(file, options?, ct)`.
  Self-contained HTML with inline CSS, collapsible `<details>` rounds,
  knowledge/operator sections, token statistics, printable layout.
- **F-10b `WithTimeout(TimeSpan)`** — debate-level wall-clock timeout that links
  a `CancellationTokenSource` with the caller's CT via
  `CreateLinkedTokenSource`. `DebateTimeout` exposed on executor.
- **F-10c `Persona` presets** — 6 built-in system-prompt fragments:
  `Expert`, `DevilsAdvocate`, `CautiousOptimist`, `DataDrivenAnalyst`,
  `RiskManager`, `Pragmatist`. `Persona.All` dictionary + `Persona.Resolve(name)`.
- **F-10d `CouncilBenchmark`** — `Benchmarking/CouncilBenchmark.cs` with
  `AddConfiguration(name, configure)` + `WithQuestion` + `WithMaxRounds`.
  `RunAsync` runs configs sequentially (failures recorded per-entry).
  `BenchmarkReport.ToMarkdown()` + `SaveComparisonAsync()` render side-by-side
  verdicts / token usage / latency tables.
- **F-10e `WithParticipantLimit(int)`** — guards against misconfiguration in
  dynamic DI-driven setups; throws `InvalidOperationException` on `Build()` if
  exceeded.
- 27 unit tests.

### Added — F-07 Debate Templates & Presets Library

- **`Delibera.Core/Templates/DebateTemplate.cs`** — abstract `DebateTemplateBase`
  fluent facade over `CouncilBuilder`. `DebateTemplate` static accessor with 6
  built-in templates: `ArchitectureReview`, `RiskAssessment`, `CodeReview`,
  `ProductDecision`, `SecurityAudit`, `DataArchitecture`. `DebateTemplate.Custom()`
  escape hatch to a fresh `CouncilBuilder`. `ConfigureCore` runs eagerly inside
  `WithProvider` so later fluent overrides take precedence over template defaults.
- Fluent API mirrors `ICouncilBuilder`: `WithQuestion` / `WithProvider` /
  `WithMaxRounds` / `WithTemperature` / `WithResponseLanguage` / `WithSystemPrompt` /
  `SaveResultTo` / `WithTelemetry` / `WithTimeout` / `WithParticipantLimit` /
  `WithKnowledgeKeeper` / `AddMember` / `WithChairman` + `Advanced` escape hatch.
- 21 unit tests.

### Added — F-01 Async Streaming Council

- **`ICouncilExecutor.StreamDebateAsync(CancellationToken)`** added as DIM
  with default implementation that calls `ExecuteAsync` and yields rounds at
  the end (back-compat for external implementations).
- **`CouncilExecutor.StreamDebateAsync`** override streams rounds **live**: the
  debate runs on a background `Task`; the strategy's existing
  `onRoundCompleted` callback bridges each completed round into an unbounded
  `Channel<DebateRound>` (natural backpressure — strategy won't produce round
  N+1 until the callback for N returns); the iterator reads from the channel
  and yields each round as it completes.
- `DebateRound.Total` (int?) and `IsFinal` (bool) properties added. `Total`
  stamped by `StreamDebateAsync` on every yielded round
  (`maxRounds + 1` when Chairman set, else `maxRounds`). `IsFinal` true when
  the round name contains "Verdict" or "Final", or `RoundNumber >= Total`.
- `ICouncilExecutor.LastStreamedResult` exposes the aggregated `DebateResult`
  after the stream completes (with logs/token stats).
- `OnRoundCompleted` event still fires for each round (back-compat).
- `ExecuteAsync` stays fully backward-compatible.
- 13 unit tests.

### Added — F-09 Dynamic Strategy Switching

- **`Delibera.Core/Debate/IStrategySelector.cs`** — `IStrategySelector` interface
  (`SelectNextAsync` returns null or new strategy). `DebateProgress` record
  (`CurrentRound`, `MaxRounds`, `CompletedRounds`, `ResponseDiversityScore`,
  `IsStalemate`). `AdaptiveStrategySelector` built-in implementation:
  - `Initial` + `OnStalemate` strategies (`required`).
  - `StagnationThreshold` (default 2) consecutive low-diversity rounds.
  - `StagnationScore` cutoff (default 0.3).
  - Switches once per debate, then resets via `Reset()`.
  - Falls back to Levenshtein text-similarity when no embedding provider
    is configured (diversity score = 0.0 sentinel).
  - No switch on last round.
- **`DebateRound.StrategyUsed`** (`IDebateStrategy?`) — audit trail stamped on
  every round when a selector is configured.
- **`CouncilBuilder.WithAdaptiveStrategy(IStrategySelector)`** + `ICouncilBuilder`
  surface — sets `Initial` strategy as starting strategy when
  `AdaptiveStrategySelector` is used.
- `CouncilExecutor` instruments the round callback to compute response
  diversity (Levenshtein-based fallback), invoke `SelectNextAsync` after each
  round, log strategy switch, stamp `StrategyUsed` on every round.
- 16 unit tests.

### Added — F-02 Pluggable Vote / Consensus Engine

- **`Delibera.Core/Voting/IVotingStrategy.cs`** — `IVotingStrategy` interface
  (`MethodName` + `TallyAsync`). `RankedOption`, `ParticipantBallot`,
  `VotingResult` records. 3 built-in strategies:
  - **`MajorityVotingStrategy`** — top-ranked option gets 1 point each.
  - **`BordaCountVotingStrategy`** — N-1 points for top, N-2 for second, etc.
  - **`WeightedVotingStrategy`** — per-member `MemberWeights` overrides.
- **`Chairman.CreateVoting(model, provider, strategy)`** factory encodes the
  strategy in the Chairman's `PersonaPrompt` via `VotingChairmanMarker` so
  `CouncilExecutor` can detect it at runtime.
- **`CouncilBuilder.WithVotingChairman(model, provider, strategy)`** +
  `ICouncilBuilder.WithVotingChairman`.
- `DebateResult.VotingTally` (`VotingResult?`) property. `ToMarkdown()` renders
  a `🗳️ Voting Tally` section with method, winning option, and full score table.
- `CouncilExecutor.RunVotingAsync` extracts numbered/bulleted options from the
  final round's responses, asks each member to rank them, parses `'1,2,3'`
  replies, builds `ParticipantBallot`s, tallies via `IVotingStrategy.TallyAsync`.
- `WeightedVotingStrategy` validates non-negative weights and at least one
  positive weight.
- 16 unit tests.

### Added — F-05 Structured Output / JSON Schema

- **`Delibera.Core/Output/IStructuredOutputSerializer.cs`** — `IStructuredOutputSerializer`
  interface (`GenerateSchema<T>` + `Deserialize<T>`). `JsonSchemaOutputSerializer`
  default implementation:
  - Uses .NET 10 `JsonSchemaExporter.GetJsonSchemaAsNode` for schema generation.
  - `ExtractJson` helper strips markdown code fences and surrounding prose.
  - `BuildStructuredPrompt` appends schema + type name to the synthesis prompt.
  - `BuildCorrectionPrompt` builds the retry prompt on deserialisation failure.
  - Default `TypeInfoResolver` set for .NET 10 compatibility.
- **`ICouncilExecutor.ExecuteTypedAsync<TVerdict>(CancellationToken)`** added as
  DIM with default implementation that uses `DebateResult.GetTypedVerdict<T>`.
- `CouncilExecutor` overrides `ExecuteTypedAsync<T>`: runs `ExecuteAsync`, attempts
  to deserialise existing `FinalVerdict`, on failure re-prompts Chairman with
  correction prompt + schema, stamps `TypedVerdict` on `DebateResult` on success.
  One automatic retry with failure logging.
- `DebateResult.TypedVerdict` (`object?`) + `GetTypedVerdict<TVerdict>()` helper.
- `CouncilBuilder.WithStructuredOutput<TVerdict>(serializer?)` +
  `ICouncilBuilder.WithStructuredOutput<TVerdict>`.
- 21 unit tests.

### Added — F-03 Debate Persistence & Resume

- **`Delibera.Core/Persistence/`** — new namespace with:
  - **`IDebateStore`** interface (`Save` / `Load` / `List` / `Delete`).
  - **`DebateCheckpoint`** record (`DebateId`, `CreatedAt`,
    `LastCompletedRound`, `CompletedRounds`, `Options` snapshot,
    `OriginalQuestion`).
  - **`DebateCheckpointMeta`** lightweight metadata record.
  - **`GenerateId()`** — ULID-style 26-char lexicographically-sortable ID
    (timestamp prefix + random suffix).
  - **`CreateEmpty()`** — factory for fresh checkpoints.
  - **`FileDebateStore`** — atomic JSON write-temp → rename; optional
    `RetentionDays`; thread-safe with `SemaphoreSlim`; lazy retention sweep on
    `ListAsync`.
  - **`InMemoryDebateStore`** — `ConcurrentDictionary`-backed, for testing.
- **`CouncilOptions.Persistence`** sub-section with `PersistenceOptions`:
  `Enabled`, `Store` (File/InMemory), `Directory`, `RetentionDays`,
  `ResumeFromDebateId`.
- `CouncilBuilder.WithPersistence(IDebateStore)` + `ResumeFrom(debateId)` +
  `ICouncilBuilder`.
- `CouncilExecutor.SaveCheckpointAsync`: after each round, builds a checkpoint
  with completed rounds, options snapshot, and reuses existing debate id
  (from `ResumeFrom` or a same-question match). Errors during save are reported
  via `ReportError` (don't abort debate).
- 24 unit tests.

### Added — F-04 Agent Memory & Long-Term Context

- **`Delibera.Core/Memory/IAgentMemory.cs`** — `IAgentMemory` interface
  (`Store` / `Recall` / `Delete`). `MemoryEntry` record (`Content`, `CreatedAt`,
  `Metadata`). 3 implementations:
  - **`InMemoryAgentMemory`** — `ConcurrentDictionary`-backed, Jaccard
    token-overlap similarity (no embedding provider required).
  - **`QdrantAgentMemory`** — per-agent collection, uses `IRagProvider`
    `SearchAsync` + `IEmbeddingProvider` for semantic similarity.
  - **`PgVectorAgentMemory`** — shared table filtered by `agent_name` metadata.
- `CouncilBuilder.WithAgentMemory(IAgentMemory?)` + `ICouncilBuilder` — null
  parameter defaults to `InMemoryAgentMemory` (no persistence).
- `CouncilExecutor`:
  - **Pre-execution**: recalls each member's top-3 memories, dedupes by content,
    prepends a `Memory from previous sessions` block to the system prompt for
    all participants.
  - **Post-execution**: stores each member's last response and the Chairman's
    final verdict as `MemoryEntry` records, tagged with member display name +
    debate id (when persistence enabled).
  - Both phases log to `ExecutionLog` under source `AgentMemory`. Errors during
    recall/store are reported but do not abort the debate.
- 13 unit tests.

### Added — ConsoleApp demos

9 new `--flag` demo entries:
- `--telemetry` (**TelemetryExample**) — in-process `ActivityListener` +
  `MeterListener` printing every span and metric.
- `--quick-wins` (**QuickWinsExample**) — HTML export, timeout, personas,
  benchmark, participant limit.
- `--templates` (**TemplatesExample**) — `ArchitectureReview` template
  against a live Ollama endpoint.
- `--stream` (**StreamingCouncilExample**) — `IAsyncEnumerable<DebateRound>`
  live output with round-by-round progress.
- `--adaptive-strategy` (**AdaptiveStrategyExample**) — `StandardDebate` →
  `CritiqueDebate` switch on stagnation.
- `--voting` (**VotingExample**) — `WeightedVotingStrategy` with per-member
  weights.
- `--structured-output` (**StructuredOutputExample**) — `ArchitectureDecision`
  typed verdict.
- `--persistence` (**PersistenceExample**) — `FileDebateStore` with retention
  + auto-resume on existing checkpoint.
- `--agent-memory` (**AgentMemoryExample**) — `InMemoryAgentMemory` across
  successive debates.

### Changed

- Bumped `Delibera.Core` package version `10.2.5` → `10.2.6`.
- `CouncilMember.PersonaPrompt` property now assigned in the constructor
  (was previously null — fixed for F-02 voting chairman detection).
- New `DebateRound.Total` and `IsFinal` properties; `StrategyUsed` property
  (nullable).
- New `DebateResult.TypedVerdict` and `VotingTally` properties; Markdown
  output gains `🗳️ Voting Tally` section.
- `<Description>` and `<PackageTags>` updated to mention new features.

### Compatibility

- **No breaking changes.** Every new feature is opt-in via additional builder
  methods (`WithTelemetry`, `WithTimeout`, `WithVotingChairman`,
  `WithStructuredOutput<T>`, `WithPersistence`, `WithAgentMemory`,
  `WithAdaptiveStrategy`). The fluent API is fully backward compatible.

### Test summary

- 9 new test files, 163 new tests, **all 259 tests pass** (up from 105 in
  10.2.5).

## [10.2.4] - 2026

This release delivers **full cooperative `CancellationToken` support across every
public async method** — a single cancel signal now aborts the entire pipeline
(rounds, Chairman synthesis, LLM calls, MCP tool invocations, RAG queries and
file writes) via `OperationCanceledException`. It also adds **in-memory
`MarkdownKnowledgeBase` loaders** that ingest markdown bodies without temp
files, with optional per-source metadata.

This release delivers **full cooperative `CancellationToken` support across every
public async method** — a single cancel signal now aborts the entire pipeline
(rounds, Chairman synthesis, LLM calls, MCP tool invocations, RAG queries and
file writes) via `OperationCanceledException`. It also adds **in-memory
`MarkdownKnowledgeBase` loaders** that ingest markdown bodies without temp
files, with optional per-source metadata.

### Added — In-memory `MarkdownKnowledgeBase` loaders

- **`KnowledgeDocument`** — new sealed record in `Delibera.Core.Knowledge`
  (`string Name`, `string Content`, `IReadOnlyDictionary<string, string>? Metadata`)
  used to tag documents with per-source metadata so the council can distinguish
  "contract" from "discovery context" inside the KB.

- **`MarkdownKnowledgeBase.LoadTextAsync(string content, string sourceName, CancellationToken)`**
  — ingest a markdown body without writing a temp file. The `sourceName`
  appears in the council's per-round context, just like a file path would.

- **`MarkdownKnowledgeBase.LoadTextAsync(KnowledgeDocument, CancellationToken)`**
  — overload that preserves the supplied `Metadata` on the indexed document.

- **`MarkdownKnowledgeBase.LoadTextsAsync(IEnumerable<KnowledgeDocument>, CancellationToken)`**
  — sequential bulk ingest with cooperative cancellation checked between
  documents.

- **`MarkdownKnowledgeBase.DocumentMetadata`** — read-only snapshot
  (`IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>?>`) of the
  per-source metadata captured at load time. Sources loaded through the
  file-path API appear as `null` entries.

### Added — Full cooperative `CancellationToken` support

Every public async method in the library now honors a `CancellationToken`
cooperatively. A single cancel signal aborts the entire pipeline — rounds,
Chairman synthesis, LLM calls, MCP tool invocations, RAG queries and even
file writes — via `OperationCanceledException`.

#### Changed — Additive CT parameters (no breaking binary changes)

- **`IKnowledgeBase.LoadAsync(string, CancellationToken)`** and
  **`IKnowledgeBase.LoadManyAsync(IEnumerable<string>, CancellationToken)`** —
  `CancellationToken ct = default` added to existing method signatures.
  **Source-breaking** for any external implementer of the interface;
  **binary-compatible** for callers (parameter has a default value).

- **`MarkdownKnowledgeBase`** — the file-path API
  (`LoadAsync`, `LoadManyAsync`, `LoadDirectoryAsync`) now accepts a
  `CancellationToken` and forwards it to `File.ReadAllTextAsync` and between
  files in bulk operations.

- **`DebateResult`** — all save methods
  (`SaveToMarkdownAsync`, `SaveStatisticsAsync`, `SaveLogsAsync`, `SaveAllAsync`,
  `SaveToFileAsync`) accept a `CancellationToken` and forward it to
  `File.WriteAllTextAsync`. `SaveAllAsync` propagates the token to each
  individual save call.

- **`CouncilExecutor.ExecuteAsync`** — the caller's `CancellationToken` is now
  forwarded to the final `result.SaveToFileAsync(_outputPath, ct)` step,
  closing the top-level cancellation chain end-to-end.

#### Added — Hosted-service helper

- **`Delibera.Core.Extensions.IAppStoppingToken`** — minimal abstraction
  (`CancellationToken ApplicationStopping { get; }`) that any host lifetime
  can implement in three lines without forcing a `Microsoft.Extensions.Hosting`
  dependency on `Delibera.Core`.

- **`Delibera.Core.Extensions.CouncilExecutorLifetimeExtensions.ExecuteAsync(
  ICouncilExecutor, IAppStoppingToken, CancellationToken)`** — links the
  caller's `CancellationToken` with `IAppStoppingToken.ApplicationStopping`
  via `CancellationTokenSource.CreateLinkedTokenSource` so a host shutdown
  (ASP.NET Core, Worker Service, etc.) cancels the debate cooperatively.
  Whichever signal fires first wins.

#### Added — ConsoleApp demo

- **`Delibera.ConsoleApp.Examples.CancellationExample`** — run with
  `--cancellation`. Wires `Console.CancelKeyPress` to a
  `CancellationTokenSource` and forwards the token through
  `executor.ExecuteAsync(cts.Token)`. Includes a heartbeat task that proves
  the main thread is alive while the debate is awaiting.

- The default `Program.Main` now also wires Ctrl+C for the main demo path
  so a user can cancel any run with a single keystroke. Example-mode
  dispatchers (e.g. `--cancellation`) keep ownership of their own
  `CancelKeyPress` handlers and are not double-bound.

### Tests

- **9 new tests** in `MarkdownKnowledgeBaseTests` for the in-memory loaders:
  string overload happy-path, `KnowledgeDocument` overload happy-path with
  metadata assertion, `LoadTextsAsync` bulk happy-path, source-name
  overwrite semantics, and **6 cancellation tests** covering pre-canceled
  tokens on every public method plus `LoadManyAsync` / `LoadDirectoryAsync`
  honoring cancellation between files.
- **7 new tests** in `DebateResultCancellationTests` covering all save
  methods with pre-canceled tokens + a happy-path test for `SaveAllAsync`
  that verifies the three files are produced.
- **2 new tests** in `CouncilExecutorCancellationTests` covering the
  executor's CT-aware save path and a positive no-cancel run.
- **5 new tests** in `CouncilExecutorLifetimeExtensionsTests` covering the
  `IAppStoppingToken` helper: caller-token respected, application-stopping
  token respected, run-to-completion when neither is canceled, and
  null-argument validation on both `executor` and `lifetime`.

### Documentation

- New **Cancellation Support** section in `Delibera.Core/README.md` with 4
  worked examples (timeout, manual cancel, ASP.NET Core
  `IHostApplicationLifetime` adapter, Console Ctrl+C) and a table of
  cancellable operations.
- New **Cancellation Support** section in the repo-root `README.md` with a
  quick-start example and a link to the full guide.
- Feature row added to the Key Features table in both READMEs.
- `MarkdownKnowledgeBase.LoadTextAsync` and `LoadTextsAsync` added to the
  cancellable-operations table in `Delibera.Core/README.md`.

### Compatibility

- **No breaking binary changes.** All new `CancellationToken` parameters
  have a default value, so existing compiled callers continue to link and
  run unchanged. External implementers of `IKnowledgeBase` must add the
  `CancellationToken` parameter to their method signatures — this is a
  source-level break for that scenario only.
- **No behavior change** for callers that don't pass a token: the
  pre-v10.2.4 behavior is preserved exactly.

## [10.2.3] - 2026

### Added — AutoChunking (progressive disclosure for large documents)

- **AutoChunking** — automatic splitting of large knowledge documents (contracts, reports, articles)
  into context-window-sized chunks distributed across debate rounds via progressive disclosure.
  When a document exceeds the smallest model's context window, the orchestrator creates a
  `ChunkingPlan` and distributes chunks evenly across rounds so every model receives a complete
  view of the document by the final round.

- **`Chunking` namespace** (`Delibera.Core.Chunking`):
  - `AutoChunker` — static planner with 3 strategies: `SemanticBoundary` (respects Markdown
    headers → paragraphs → sentences), `FixedSize`, `SlidingWindow` (50% overlap).
  - `AutoChunkingOrchestrator` — analyses model capabilities, calculates overhead, creates
    the plan, and distributes chunks per round.
  - `AutoChunkingOptions` — configuration: strategy, safety margin, max chunks/round,
    Map-Reduce toggle, progressive disclosure toggle.
  - `ChunkingPlan` / `DocumentChunk` — immutable records describing the split.

- **`ModelCapabilities`** record — context window, max output tokens, vision/tool support,
  model family. Obtained from providers via the new `ILLMProvider.GetModelCapabilitiesAsync()`.

- **`ModelContextWindowRegistry`** — static registry of 40+ popular models (Llama, Qwen,
  DeepSeek, Phi, Mistral, Gemma, GPT, Claude, YandexGPT, …) with their context window sizes.
  `Register()` for custom models. Case-insensitive substring matching.

- **`ILLMProvider.GetModelCapabilitiesAsync(string model, CancellationToken)`** — new
  default interface method (returns `null`). Overridden by:
  - `OllamaProvider` — queries `/api/show` and extracts `num_ctx` from Modelfile parameters
    via regex. Falls back to `ModelContextWindowRegistry`.
  - `ChatClientLLMProvider` / `YandexGptProvider` — fall back to `ModelContextWindowRegistry`.

- **`PromptContext` extended** — new fields: `ChunkingPlan`, `AutoChunkingEnabled`,
  `MinContextWindow`. New method `GetChunkedUserPrompt(roundNumber, totalRounds, previousRounds)`
  returns round-appropriate chunks with `[Chunk X/Y]` markers and section titles.

- **`DebateScenario.BuildChunkedPrompt()`** — protected helper used by all built-in strategies
  (`StandardDebate`, `CritiqueDebate`, `ConsensusDebate`). Replaces `GetFullUserPrompt()` calls
  with round-aware chunk distribution.

- **`CouncilBuilder` bulk configuration**:
  - `WithOptions(CouncilOptions options)` — apply a pre-built options snapshot.
  - `WithOptions(Action<CouncilOptions> configure)` — inline lambda configuration.
  - `CouncilBuilder(CouncilOptions options)` constructor — one-shot setup.
  - `WithAutoChunking(AutoChunkingOptions?)` — enable chunking via fluent API.
  - `WithModelContextWindow(pattern, tokens)` — register custom model context windows.
  - `ApplyOptions()` — transfers all non-default `CouncilOptions` fields to the builder.

- **`ICouncilBuilder` updated** — new methods: `WithOptions(CouncilOptions)`,
  `WithOptions(Action<CouncilOptions>)`, `WithAutoChunking(...)`, `WithModelContextWindow(...)`.

- **`AutoChunkingConfig`** in `CouncilOptions` — bound from `Delibera:AutoChunking`
  configuration section. Fields: `Enabled`, `Strategy`, `SafetyMargin`, `MaxChunksPerRound`,
  `EnableMapReduce`, `EnableProgressiveDisclosure`, `ModelContextWindows` (dictionary).
  `ToOptions()` converts to `AutoChunkingOptions` and registers custom model windows.

- **DI auto-wiring** — `AddDelibera(IConfiguration, ILoggerFactory, ...)` now resolves
  `IOptions<CouncilOptions>` and passes it to `new CouncilBuilder(options)`, so all
  settings (strategy, rounds, temperature, compression, auto-chunking, etc.) are applied
  automatically. Explicit builder calls take precedence.

- **Console example** `AutoChunkingExample` (run with `--autochunking`) demonstrating:
  - 3 configuration paths: fluent API, options snapshot, lambda.
  - Offline chunking plan demo for 4K/8K/32K/128K context windows.
  - Model context window registry dump.
  - Synthetic contract document (~15K+ chars) that triggers chunking on small-context models.

### Changed

- Bumped `Delibera.Core` package version `10.2.2` → `10.2.3`.
- `CouncilExecutor` constructor now accepts optional `AutoChunkingOptions?` parameter.
- `CouncilExecutor.ExecuteAsync()` invokes `AutoChunkingOrchestrator.PrepareContextAsync()`
  before the debate when AutoChunking is enabled.
- `CouncilExecutor.GetInfo()` displays AutoChunking configuration when active.
- `appsettings.json` updated with `AutoChunking` section.

### Compatibility

- **No breaking changes.** AutoChunking is opt-in — disabled by default. All existing
  `ILLMProvider` implementations continue to work (default `GetModelCapabilitiesAsync`
  returns `null`). `PromptContext` is a `record` with `with`-expression support, so
  existing code that constructs it directly is unaffected. The new `CouncilExecutor`
  constructor parameter is optional.

### Added — Polly v8 resilience via Microsoft.Extensions.Http.Resilience

- **Microsoft.Extensions.Http.Resilience 10.7.0** dependency (transitively brings Polly v8
  `Polly.Core` + `Microsoft.Extensions.Http`). Hand-rolled retry loops in `OllamaProvider`,
  `YandexGptProvider`, and `McpClientAdapter` have been **removed** in favour of named
  Polly v8 pipelines registered through DI.

- **`ResilienceOptions`** (bound from `Delibera:Resilience` configuration section)
  configures: `MaxRetryAttempts`, `BaseDelay`, `MaxDelay`, `UseJitter`, `BackoffType`
  (`"Exponential"` / `"Linear"` / `"Constant"`), `RetryableStatusCodes`, `AttemptTimeout`,
  master `Enabled` flag. All values are live-tracked through `IOptionsMonitor<>` so option
  changes are honoured without rebuilding the container.

- **`IDeliberaResiliencePipelineProvider`** — central registry of named Polly v8
  pipelines with three built-in keys:
  - `Delibera.Local` — retries connection-level failures only (no status code); used by
    `OllamaConnectionMode.Local`.
  - `Delibera.Cloud` — retries transient HTTP responses (configurable allow-list,
    default `[408, 429, 500, 502, 503, 504, 524]`) plus `HttpRequestException` /
    `TaskCanceledException`; used by `OllamaConnectionMode.Cloud`, `YandexGptProvider`,
    and `McpClientAdapter`'s HTTP transport.
  - `Delibera.Default` — alias for the more permissive of the two; used when a consumer
    does not specify a pipeline key.

- **`AddDeliberaResilience(IServiceCollection, Action<ResilienceOptions>?)`** —
  one-call DI setup. Registers `IDeliberaResiliencePipelineProvider`, plus three named
  `HttpClient` entries (`Delibera.Ollama.Local`, `Delibera.Ollama.Cloud`,
  `Delibera.YandexGPT`) each wired with `AddResilienceHandler` so retries apply to the
  HttpClient handler chain (the standard `Microsoft.Extensions.Http.Resilience` pattern).

- **`AddDeliberaResiliencePipeline(name, build)`** — register custom Polly v8
  pipelines under arbitrary keys. The pipeline registry merges them into its lookup
  table alongside the built-ins.

- **`AddDeliberaHttpClient(name, pipelineName, configure)`** — register an arbitrary
  named HttpClient whose handler pipeline is decorated with the chosen
  Polly v8 pipeline. Useful for additional HTTP integrations beyond Delibera's
  built-in providers.

- **`OllamaProvider` DI path** — `OllamaProvider.ForLocal(endpoint, IHttpClientFactory,
  IDeliberaResiliencePipelineProvider, ...)` and `ForCloud(...)` overloads construct
  the provider from the factory's named HttpClient and the operation-level pipeline
  (`GetOperationPipeline`). The hand-rolled `for` loop and `IsTransientHttp` helper
  have been deleted; transient failures are now retried by the configured pipeline.

- **`YandexGptProvider` DI path** — new constructor accepts `IHttpClientFactory?` +
  `IDeliberaResiliencePipelineProvider?`. The original `(apiKey, folderId, ...)`
  constructor remains for backward compatibility — providers constructed without DI
  still work, just without retry.

- **`McpClientAdapter` DI path** — new constructor accepts `IHttpClientFactory?` +
  logical client name. When wired, the adapter injects the factory-managed HttpClient
  into the ModelContextProtocol `HttpClientTransport` so retries apply to MCP HTTP/SSE
  traffic.

- **`ResilientHttpClientExtensions.SendAsync(http, pipeline, request, ...)`** —
  extension helper that runs an `HttpClient` request through a typed
  `ResiliencePipeline<HttpResponseMessage>`. Handles request cloning between attempts
  (HttpClient disposes the request after the first attempt).

- **Console example** `ResilienceExample` (run with `--resilience`) showing how to
  wire up `AddDeliberaResilience`, register a custom pipeline, and read the live
  configuration through `IOptionsMonitor`.

- **Tests** — 9 new unit tests in `ResilienceTests` covering `ResilienceOptions`
  defaults, the pipeline registry, `AddDeliberaResilience` DI wiring, configuration
  binding from `Delibera:Resilience`, and custom-pipeline registration.

### Changed

- Bumped `Delibera.Core` package version `10.2.0` → `10.3.0`.
- Bumped console app dependencies to `Microsoft.Extensions.Http` 10.0.9 +
  `Microsoft.Extensions.Http.Resilience` 10.7.0.
- `OllamaProvider.Client` is now constructed eagerly inside the provider's
  constructor (was lazy before). `OllamaEmbeddingProvider`'s constructor still reads
  `ollamaProvider.Client` synchronously, so the eager construction preserves the
  existing pattern.
- `ResilienceOptions` is bound from the `Delibera:Resilience` configuration section
  inside `AddDelibera(IConfiguration, ...)` so `IOptionsMonitor<ResilienceOptions>`
  flows through DI transparently.

### Compatibility

- **No breaking changes for non-DI consumers.** The legacy `OllamaProvider(endpoint,
  apiKey, ...)`, `YandexGptProvider(apiKey, folderId, ...)`, and `McpClientAdapter(config)`
  constructors remain in place and behave exactly as before — without retries (the
  behaviour before v10.2.2). Consumers that want retry semantics opt in by using the
  new DI-aware overloads.
- **No breaking changes for DI consumers either** unless they previously relied on
  the hand-rolled retry semantics in `OllamaProvider.ChatAsync` (not exposed
  publicly; the loop was internal).

## [10.2.0] - 2026

### Added — Microsoft.Extensions.Logging, response-language enforcement, parallel Operator

- **Microsoft.Extensions.Logging support** — inject your own `ILogger` / `ILoggerFactory` and every
  debate event (Chairman opening, Knowledge Keeper queries, compression, Operator interactions,
  participant responses, errors) is forwarded to the host's logging pipeline. New APIs:
  - `ICouncilBuilder.WithLogger(ILogger?)` and `CouncilBuilder.WithLogger(...)`.
  - `ICouncilExecutor.Logger` property exposing the configured `ILogger`.
  - `AddDelibera(IServiceCollection, IConfiguration, ILoggerFactory, string?)` DI overload that
    auto-decorates every resolved `ICouncilBuilder` with a logger.
  - `DebateExecutionOptions` record bundles the logger (plus response language + parallelism)
    threaded through `IDebateStrategy.ExecuteAsync(...)` via a new
    `IDebateStrategyWithOptions` interface (default method on `IDebateStrategy` keeps custom
    strategies working unchanged).
- **Response-language enforcement** — `ICouncilBuilder.WithResponseLanguage(string?)` and
  `CouncilOptions.ResponseLanguage`. When set, Delibera injects a strict directive into every
  system and user prompt so all models (participants, Chairman, Knowledge Keeper, Operator) answer
  exclusively in the chosen language, regardless of the prompt or retrieved RAG context.
- **Parallel Operator requests** — `[[OPERATOR: …]]` tasks delegated by participants within a round
  now run concurrently via `Parallel.ForEachAsync`, bounded by
  `ICouncilBuilder.WithMaxDegreeOfParallelism(int)` / `CouncilOptions.MaxDegreeOfParallelism`
  (0 = unbounded, default). Delibera-shipped strategies (`StandardDebate`, `CritiqueDebate`,
  `ConsensusDebate`) opt in via the new `ExecuteAsync(..., DebateExecutionOptions, ...)` overload.

### Changed

- **Renamed `LogLevel` enum to `ExecutionLogLevel`** (in `Delibera.Core.Models`) to avoid a name
  clash with `Microsoft.Extensions.Logging.LogLevel`, which is now referenced throughout the
  framework. The `ExecutionLog.Level` field, `DebateResult.ToLogsMarkdown()`, and the console
  demo all use the renamed enum. `ExecutionLog.ToMicrosoftLogLevel()` maps to the M.E.Logging
  severity. This is a source-breaking change for consumers that referenced `LogLevel.Info` etc.
  directly; replace with `ExecutionLogLevel.Info`.

## [10.1.1] - 2026

### Added — Microsoft.Extensions.AI integration

- **`Microsoft.Extensions.AI` 10.7.0** dependency in `Delibera.Core`.
- **`ChatClientLLMProvider`** — adapts any Microsoft.Extensions.AI `IChatClient` to Delibera's
  `ILLMProvider`. Works with OpenAI, Azure OpenAI, Ollama, Anthropic and local OpenAI-compatible
  servers (LM Studio, LocalAI, vLLM) without a bespoke provider per vendor.
- **`EmbeddingGeneratorProvider`** — adapts any `IEmbeddingGenerator<string, Embedding<float>>`
  to Delibera's `IEmbeddingProvider` for RAG indexing/querying.
- **`ILLMProvider.ChatStreamAsync(...)`** — additive default interface method for token-by-token
  streaming. `ChatClientLLMProvider` overrides it for true streaming; existing providers fall back
  to a single `ChatAsync` call, so nothing breaks.
- **`MicrosoftAIExtensions`** bridge helpers:
  - `IChatClient.AsLLMProvider(...)`, `IEmbeddingGenerator.AsEmbeddingProvider(...)`
  - `ILLMProvider.AsChatClient(...)` (reverse bridge so Delibera providers can join a
    Microsoft.Extensions.AI middleware pipeline)
  - `IChatClient.WithMiddleware(...)` to compose function invocation + logging.
- **`ProviderFactory.CreateFromChatClient(...)`** — build a cached provider directly from an `IChatClient`.
- **`OllamaProvider.AsChatClient()` / `AsEmbeddingGenerator()`** — expose the underlying
  OllamaSharp client (which natively implements the Microsoft.Extensions.AI interfaces).
- **DI helpers** `AddDeliberaChatClient(...)` and `AddDeliberaEmbeddingGenerator(...)` in
  `ServiceCollectionExtensions`.
- **Console example** `MicrosoftExtensionsAiExample` (run with `--msai`).
- **Unit test project** `tests/Delibera.Core.Tests` (xUnit) covering the new providers, the bridge
  adapters and the factory — plus a solution file `Delibera.slnx`.

### Changed

- Bumped `Delibera.Core` package version `10.1.0` → `10.1.1`.
- Documentation: `README.md` / `README-RU.md` and `docs/QuickStart*.md` updated with a
  Microsoft.Extensions.AI section.

### Compatibility

- **No breaking changes.** The public API is fully backward compatible; all existing
  `ILLMProvider` / `IEmbeddingProvider` consumers continue to work unchanged.

## [10.1.0] - 2026

- Operator (MCP tools) role, .NET 10 / C# 15 (preview) upgrade, high-performance hot-path
  optimizations (SIMD cosine similarity, allocation-free token counting, pooled cache hashing).
- Dependency injection, context compression, and RAG (Qdrant, pgvector) support.
