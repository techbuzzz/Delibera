# Changelog

All notable changes to **Delibera** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [10.2.4] - 2026

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
