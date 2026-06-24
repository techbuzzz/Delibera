# Changelog

All notable changes to **Delibera** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
