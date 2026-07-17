# Delibera v10.3.0 — What's New

> **Status:** ✅ Shipped — July 2026
> **402 unit tests pass** (337 Core + 65 Server).
> **Breaking changes** — see the migration guide below.

This document covers the three major features and breaking changes in Delibera v10.3.0.
For the full changelog, see [CHANGELOG.md](../CHANGELOG.md). For the roadmap, see [docs/ROADMAP.md](ROADMAP.md).

---

## Table of contents

- [P-01 — Breaking Changes](#p-01--breaking-changes)
- [S-01 — Distributed Debates](#s-01--distributed-debates)
- [S-03 — Result Caching](#s-03--result-caching)
- [Delibera.Server — ASP.NET Core Hosted Service](#deliberaserver--aspnet-core-hosted-service)
- [Pre-merge Cleanup](#pre-merge-cleanup)

---

## P-01 — Breaking Changes

v10.3.0 removes deprecated APIs that were marked `[Obsolete]` since v10.1. If you're upgrading from v10.2.x, make the following changes:

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
| `DebateStatus.Paused` enum value | Removed — never assigned |
| `DebateOrchestrationStatus.Pending` enum value | Removed — never assigned |

### `WeightedVotingStrategy.ResolveWeight` fix

`ResolveWeight` now falls back to `ballot.Weight` instead of the constructor's `defaultWeight`. Per-ballot weights work as documented.

---

## S-01 — Distributed Debates

Delibera now supports distributed debate orchestration. The `IDebateOrchestrator` abstraction decouples debate execution from the transport layer, enabling both in-process and Redis-based deployments.

### Core interface

```csharp
public interface IDebateOrchestrator
{
    Task<DebateResult> ExecuteAsync(ICouncilBuilder builder, CancellationToken ct = default);
    Task<DebateHandle> EnqueueAsync(string debateId, ICouncilBuilder builder, CancellationToken ct = default);
    Task<DebateHandle?> GetStatusAsync(string debateId, CancellationToken ct = default);
    IAsyncEnumerable<DebateRoundEvent> StreamAsync(string debateId, CancellationToken ct = default);
    Task<bool> CancelAsync(string debateId, CancellationToken ct = default);
}
```

### Local mode (default)

```csharp
services.AddDelibera(configuration);
// LocalDebateOrchestrator is registered by default
```

### Redis mode

```csharp
services.AddDelibera(configuration);
services.AddRedisDebateOrchestrator(configuration);
```

Round events are published to Redis Streams so any connected API server can stream them via SSE.

See [docs/distributed-debates.md](distributed-debates.md) for the full architecture diagram and configuration reference.

---

## S-03 — Result Caching

Avoid re-running identical debates. The cache key is a SHA-256 hash of the question, members, strategy, and configuration.

### Cache Behavior

| Mode | Read | Write | Use Case |
|------|------|-------|----------|
| `Disabled` | No | No | Default — no caching |
| `ReadWrite` | Yes | Yes | Production — return cached or execute & cache |
| `ReadOnly` | Yes | No | Stale cache is acceptable, don't cache new results |
| `WriteThrough` | No | Yes | Always execute, always cache (warm-up) |
| `Bypass` | No | No | Force fresh execution even when cache is configured |

### Quick Start

```csharp
// In-Memory (development)
services.AddDelibera(configuration)
    .UseInMemoryCache(ttl: TimeSpan.FromHours(1));

// File-based
services.AddDelibera(configuration)
    .UseFileCache(directory: "./debate-cache", ttl: TimeSpan.FromDays(7));

// Redis (production)
services.AddRedisDebateOrchestrator(configuration);
services.UseRedisCache(ttl: TimeSpan.FromHours(24));
```

### Per-debate control

```csharp
var executor = new CouncilBuilder()
    .WithUserPrompt("What is the best architecture?")
    .AddMember("gpt-4o", provider, "Architect")
    .WithCacheBehavior(CacheBehavior.ReadWrite)
    .Build();

var result = await executor.ExecuteAsync();
Console.WriteLine($"Cache hit: {result.CacheHit}");  // true if served from cache
Console.WriteLine($"Cache key: {result.CacheKey}");   // e.g. "A3F2B8C1D4E5F6A7"
Console.WriteLine($"Cached at: {result.CachedAt}");   // original cache timestamp
```

See [docs/caching.md](caching.md) for the full reference.

---

## Delibera.Server — ASP.NET Core Hosted Service

A new `Delibera.Server` project provides a self-hosted ASP.NET Core 10 Minimal API:

- **REST API**: `POST /api/v1/debates` (sync), `POST /api/v1/debates/async` (fire-and-forget), `GET /api/v1/debates/{id}`, `DELETE /api/v1/debates/{id}`
- **SSE streaming**: `GET /api/v1/debates/{id}/stream` — powered by `IDebateOrchestrator.StreamAsync()`
- **Scenarios**: `POST /api/v1/scenarios` for ad-hoc debates
- **OpenAPI**: Swagger UI at `/openapi`

See [docs/Server.md](Server.md) for the full API reference.

---

## Pre-merge Cleanup

- Removed `DebateStatus.Paused` and `DebateOrchestrationStatus.Pending` — dead enum values never assigned
- Fixed `DebateRecord.Label` CS8618 warning (now defaults to `string.Empty`)
- `DebateResponse` now includes `Label`, `CacheHit`, and `CacheKey` fields
- `SseDebateStreamWriter` rewritten to consume `IDebateOrchestrator.StreamAsync()` instead of `DebateRecord.RoundReader`
- `DebateOrchestrationService` rewritten from 200ms polling to event-driven `StreamAsync()`
- All CS1591/CS1574 XML-doc warnings resolved across `Delibera.Core`

---

## Performance & Integrity (Phases 1–3)

### Memory leak fixes

| Component | Fix |
|-----------|-----|
| `LocalDebateOrchestrator` | 30-min eviction timer for completed `DebateEntry` instances |
| `DebateOrchestrationService` | 30-min eviction timer for completed `DebateRecord` instances |
| `RedisDebateOrchestrator` | 30-min eviction timer for completed `DebateEntry` instances |
| Server templates × 5 + `ScenarioBuilder` | `ProviderFactory` now `using var` — properly disposed |
| `CompressionCache` | `IDisposable` — disposes `ReaderWriterLockSlim` |
| `FileDebateStore` | `IDisposable` — disposes `SemaphoreSlim` |

### Thread safety

- `DebateRecord.Status` and `RedisDebateOrchestrator.DebateEntry.Status` use `volatile int` + `Volatile.Read`/`Volatile.Write` for cross-thread visibility.
- `ProviderFactory.CachingFactory` uses `ConcurrentDictionary<string, T>` + `GetOrAdd` instead of `Dictionary` + manual `lock`.

### Async correctness

- `ConfigureAwait(false)` added to all `await` expressions in `Delibera.Core` and `Delibera.Redis` (~50+ sites).
- Fire-and-forget `CancelAsync` in `DebateOrchestrationService` now uses `Task.Run` with try/catch + logging.

### Allocation optimisations

- **SSE writer** — `JsonSerializer.SerializeToUtf8Bytes` + byte-level writes instead of string-based serialization.
- **Cache key generator** — `SerializeToUtf8Bytes` avoids intermediate `string` allocation.
- **`ModelContextWindowRegistry`** — `FrozenDictionary`/`FrozenSet` for O(1) lookups; `Register` invalidates the frozen cache.
- **`IDebateCache` / `IDebateStore` / `IDebateOrchestrator.GetStatusAsync`** — `ValueTask<T>` instead of `Task<T>`; synchronous backends avoid `Task` allocation.
- **Levenshtein distance** — single-row DP with `Span<int>` + `stackalloc` for strings ≤128 chars.
- **`TokenCounter`** — `ReaderWriterLockSlim` instead of `lock` for concurrent reads.
- **`RankedOption`** — `readonly record struct` instead of `sealed record` (avoids heap allocation).
- **`StringBuilder`** — capacity hints in `ToMarkdown()` (4096), `ToStatisticsMarkdown()` (2048), `ToLogsMarkdown()` (2048).