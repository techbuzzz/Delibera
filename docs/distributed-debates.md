# Distributed Debates (S-01)

Delibera supports distributed debate orchestration via the `IDebateOrchestrator` abstraction, enabling deployment scenarios from single-instance development to multi-node production with Redis Streams.

## Architecture

```
┌─────────────────┐      ┌──────────────────────┐
│  API Server      │      │  API Server (node 2)  │
│  ┌────────────┐  │      │  ┌────────────┐       │
│  │ DebateEndp.│──┼──┐   │  │ DebateEndp.│───────┤
│  └─────┬──────┘  │  │   │  └─────┬──────┘       │
│        │         │  │   │        │              │
│  ┌─────▼────────┐│  │   │  ┌─────▼────────┐    │
│  │ IDebateOrch. ││  │   │  │ IDebateOrch.  │    │
│  │ (Local/Redis)││  │   │  │ (Redis)       │    │
│  └─────┬────────┘│  │   │  └─────┬─────────┘    │
│        │         │  │   │        │              │
└────────┼─────────┘  │   └────────┼─────────────┘
         │            │            │
         ▼            ▼            ▼
    ┌─────────────────────────────────────┐
    │           Redis Streams             │
    │  ┌──────────┐  ┌───────────────┐   │
    │  │ delibera: │  │ delibera:     │   │
    │  │ jobs      │  │ events        │   │
    │  └──────────┘  └───────────────┘   │
    │  ┌──────────────────────────────┐   │
    │  │ delibera:state:{debateId}    │   │
    │  └──────────────────────────────┘   │
    └─────────────────────────────────────┘
```

## Core Interface

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

### DebateRoundEvent (discriminated union)

| Type | Description |
|------|-------------|
| `RoundCompleted(debateId, round)` | A debate round has completed |
| `DebateCompleted(debateId, result)` | The entire debate finished successfully |
| `DebateFailed(debateId, error)` | The debate failed with an error |
| `DebateCancelled(debateId)` | The debate was cancelled |

## Local Mode (Default)

The `LocalDebateOrchestrator` runs debates in-process using `CouncilExecutor`. No external infrastructure required.

```csharp
services.AddDelibera(configuration);
// LocalDebateOrchestrator is registered by default
```

## Redis Mode

The `RedisDebateOrchestrator` publishes round events to Redis Streams so any connected API server can stream them via SSE. State is persisted in Redis hashes for cross-instance queries.

### Configuration

```json
{
  "Delibera:Redis": {
    "ConnectionString": "localhost:6379,abortConnect=false",
    "JobStreamKey": "delibera:jobs",
    "EventStreamKey": "delibera:events",
    "OrchestratorConsumerGroup": "orchestrator",
    "WorkerConsumerGroup": "workers",
    "WorkerConsumerName": "worker-1",
    "BlockMs": 2000,
    "BatchSize": 10,
    "StateKeyPrefix": "delibera:state:",
    "StalledThresholdMs": 30000
  }
}
```

### Registration

```csharp
services.AddDelibera(configuration);
services.AddRedisDebateOrchestrator(configuration);
```

Or with inline configuration:

```csharp
services.AddRedisDebateOrchestrator(options =>
{
    options.ConnectionString = "redis.prod:6379";
    options.StateKeyPrefix = "myapp:debate:";
});
```

## DebateWorkerService

A `BackgroundService` that consumes jobs from the Redis `delibera:jobs` stream. In the current iteration, the `RedisDebateOrchestrator` runs debates locally and publishes events to Redis — the worker is a placeholder for future distributed turn-level execution.

Register in `Program.cs`:

```csharp
services.AddHostedService<DebateWorkerService>();
```

## Serialization

`DebateRound` contains `IDebateStrategy? StrategyUsed` which is not serializable. The `RedisSerializer` maps to/from `RedisRoundEvent` / `RedisDebateResult` DTOs that exclude non-serializable fields.