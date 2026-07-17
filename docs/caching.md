# Result Caching (S-03)

Delibera can cache `DebateResult` to avoid re-running identical debates. The cache key is derived from a SHA-256 hash of the debate-defining inputs (question, members, strategy, knowledge, configuration).

## Cache Behavior

| Mode | Read | Write | Use Case |
|------|------|-------|----------|
| `Disabled` | No | No | Default — no caching |
| `ReadWrite` | Yes | Yes | Production — return cached or execute & cache |
| `ReadOnly` | Yes | No | Stale cache is acceptable, don't cache new results |
| `WriteThrough` | No | Yes | Always execute, always cache (warm-up) |
| `Bypass` | No | No | Force fresh execution even when cache is configured |

## Quick Start

### In-Memory Cache

```csharp
services.AddDelibera(configuration)
    .UseInMemoryCache(ttl: TimeSpan.FromHours(1));
```

### File Cache

```csharp
services.AddDelibera(configuration)
    .UseFileCache(directory: "./debate-cache", ttl: TimeSpan.FromDays(7));
```

### Redis Cache

```csharp
services.AddRedisDebateOrchestrator(configuration);
services.UseRedisCache(ttl: TimeSpan.FromHours(24));
```

## Per-Debate Control

```csharp
var builder = new CouncilBuilder()
    .WithUserPrompt("What is the best architecture?")
    .AddMember("gpt-4o", provider, "Architect")
    .WithCacheBehavior(CacheBehavior.ReadWrite);  // or Bypass, WriteThrough, etc.

var executor = builder.Build();
var result = await executor.ExecuteAsync();

Console.WriteLine($"Cache hit: {result.CacheHit}");   // true if served from cache
Console.WriteLine($"Cache key: {result.CacheKey}");    // e.g. "A3F2B8C1D4E5F6A7"
Console.WriteLine($"Cached at: {result.CachedAt}");    // original cache timestamp
```

## Cache Key Generation

`DebateCacheKeyGenerator` produces a deterministic 16-character hex key from:

- User prompt (`PromptContext.UserPrompt`)
- Member display names (sorted alphabetically)
- Strategy name
- Max rounds
- Temperature
- System prompt
- Knowledge content hash (SHA-256)

```csharp
var key = DebateCacheKeyGenerator.Generate(
    context, members, "Standard", maxRounds: 4, temperature: 0.7f, systemPrompt);
// => "A3F2B8C1D4E5F6A7"
```

Two debates with identical inputs always produce the same key.

## IDebateCache Interface

```csharp
public interface IDebateCache
{
    Task<DebateResult?> GetAsync(string cacheKey, CancellationToken ct = default);
    Task SetAsync(string cacheKey, DebateResult result, TimeSpan? ttl = null, CancellationToken ct = default);
    Task InvalidateAsync(string cacheKey, CancellationToken ct = default);
    Task<bool> ExistsAsync(string cacheKey, CancellationToken ct = default);
}
```

## Implementation Details

### InMemoryDebateCache

Uses `IMemoryCache` with sliding expiration. Suitable for single-instance development and testing.

### FileDebateCache

Persists `DebateResult` as JSON files (`{cacheKey}.cache.json`) in a configurable directory. TTL is enforced via file modification time — expired files are deleted on read. Suitable for local development and CLI benchmark caching.

### RedisDebateCache

Uses `StackExchange.Redis` with `SETEX` for TTL. Shares the Redis connection with `RedisDebateOrchestrator`. Suitable for distributed/production deployments.

## OpenTelemetry

When caching is enabled, the `delibera.cache.hit` counter is incremented on each cache hit, tagged with `cache_backend` (e.g., `InMemoryDebateCache`, `FileDebateCache`, `RedisDebateCache`).

## appsettings.json

```json
{
  "Delibera": {
    "Cache": {
      "Provider": "InMemory",
      "DefaultTtlMinutes": 60,
      "FileDirectory": "./debate-cache",
      "RedisTtlHours": 24
    }
  }
}
```

| Field | Description |
|-------|-------------|
| `Provider` | `InMemory`, `File`, `Redis`, or `None` |
| `DefaultTtlMinutes` | Default TTL for cache entries |
| `FileDirectory` | Directory for `FileDebateCache` |
| `RedisTtlHours` | TTL for `RedisDebateCache` |

## DebateResponse DTO

When a debate result is served from cache, the HTTP response includes:

```json
{
  "debateId": "a1b2c3d4",
  "status": "Completed",
  "cacheHit": true,
  "cacheKey": "A3F2B8C1D4E5F6A7",
  ...
}
```