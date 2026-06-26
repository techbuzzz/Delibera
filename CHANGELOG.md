# Changelog

All notable changes to **Delibera** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [10.2.2] - 2026

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
