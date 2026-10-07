# Delibera.Redis

Redis-backed distributed debate execution for [Delibera](https://github.com/techbuzzz/Delibera).

A debate is a fan-out across several models and several rounds, which is exactly the shape that breaks on a
single box: one worker dies mid-debate and the whole run is lost. This package moves round publication onto
Redis Streams so any number of workers can pick up the same debate, survive each other's restarts, and share
state.

## What it gives you

- `RedisDebateOrchestrator` — an `IDebateOrchestrator` that publishes every completed round to a Redis Stream
  and reads them back as an async stream.
- `DebateWorkerService` — a background service that consumes debate work and streams results.
- `RedisDebateCache` — result caching backed by Redis, with TTLs.
- `RedisOrchestratorExtensions` / `RedisOrchestratorOptions` — DI wiring and connection settings.

## Usage

```csharp
using Delibera.Redis;

builder.Services.AddRedisDebateOrchestrator(options =>
{
    options.ConnectionString = configuration["Redis:ConnectionString"];
    options.StreamKeyPrefix = "delibera:rounds:";
    options.StreamMaxLength = 10_000;      // bounded, so a stream cannot grow without limit
});
```

Then pick it on the council:

```csharp
var executor = new CouncilBuilder()
    .AddMember("gpt-4o", providerA)
    .AddMember("claude", providerB)
    .WithOrchestrator(redisOrchestrator)
    .Build();
```

## Dependency

Depends on `Delibera.Core` and `StackExchange.Redis`. The package dependency is declared for you.

## Notes

Streams are created with a max length, so a debate that produces more rounds than the cap keeps the newest
events rather than growing Redis without bound. Key prefixes are configurable — do not run two deployments
against one Redis instance without setting them.

## Licence

MIT. See the repository root for details.