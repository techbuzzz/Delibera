namespace Delibera.Redis;

/// <summary>
///    Configuration options for <see cref="RedisDebateOrchestrator" />.
/// </summary>
public sealed class RedisOrchestratorOptions
{
   public const string SectionName = "Delibera:Redis";

   /// <summary>
   ///    Redis connection string (e.g. "localhost:6379,abortConnect=false").
   /// </summary>
   public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false";

   /// <summary>
   ///    Redis Streams key for member-turn jobs published by the orchestrator.
   ///    Workers read from this stream to pick up turn execution work.
   /// </summary>
   public string JobStreamKey { get; set; } = "delibera:jobs";

   /// <summary>
   ///    Redis Streams key for debate round events published by workers.
   ///    The orchestrator reads from this stream to push SSE events to clients.
   /// </summary>
   public string EventStreamKey { get; set; } = "delibera:events";

   /// <summary>
   ///    Consumer group name for the orchestrator's event listener.
   /// </summary>
   public string OrchestratorConsumerGroup { get; set; } = "orchestrator";

   /// <summary>
   ///    Consumer group name for worker instances.
   /// </summary>
   public string WorkerConsumerGroup { get; set; } = "workers";

   /// <summary>
   ///    Consumer name within the worker group. Defaults to the machine name.
   /// </summary>
   public string WorkerConsumerName { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}";

   /// <summary>
   ///    How long to block on XREADGROUP when waiting for new messages (milliseconds).
   /// </summary>
   public int BlockMs { get; set; } = 2000;

   /// <summary>
   ///    Maximum number of messages to read per XREADGROUP call.
   /// </summary>
   public int BatchSize { get; set; } = 10;

   /// <summary>
   ///    Prefix for Redis hash keys storing debate state (status, result).
   /// </summary>
   public string StateKeyPrefix { get; set; } = "delibera:state:";

   /// <summary>
   ///    Maximum number of pending messages before claiming stalled messages.
   /// </summary>
   public int MaxPendingCount { get; set; } = 100;

   /// <summary>
   ///    Time threshold (in milliseconds) after which a pending message is considered
   ///    stalled and can be claimed by another worker.
   /// </summary>
   public long StalledThresholdMs { get; set; } = 30_000;
}
