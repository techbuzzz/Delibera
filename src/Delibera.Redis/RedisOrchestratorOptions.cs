using Delibera.Core;

namespace Delibera.Redis;

/// <summary>
///    Configuration options for <see cref="RedisDebateOrchestrator" />.
/// </summary>
public sealed class RedisOrchestratorOptions
{
   public const string SectionName = BuiltIn.ConfigSections.Redis;

   /// <summary>
   ///    Redis connection string (e.g. "localhost:6379,abortConnect=false").
   /// </summary>
   public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false";

   /// <summary>
   ///    Redis Streams key for member-turn jobs published by the orchestrator.
   ///    Workers read from this stream to pick up turn execution work.
   /// </summary>
   public string JobStreamKey { get; set; } = BuiltIn.Redis.JobStreamKey;

   /// <summary>
   ///    Redis Streams key for debate round events published by workers.
   ///    The orchestrator reads from this stream to push SSE events to clients.
   /// </summary>
   public string EventStreamKey { get; set; } = BuiltIn.Redis.EventStreamKey;

   /// <summary>
   ///    Consumer group name for the orchestrator's event listener.
   /// </summary>
   public string OrchestratorConsumerGroup { get; set; } = BuiltIn.Redis.OrchestratorGroup;

   /// <summary>
   ///    Consumer group name for worker instances.
   /// </summary>
   public string WorkerConsumerGroup { get; set; } = BuiltIn.Redis.WorkerGroup;

   /// <summary>
   ///    Consumer name within the worker group. Defaults to the machine name.
   /// </summary>
   public string WorkerConsumerName { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}";

   /// <summary>
   ///    How long to block on XREADGROUP when waiting for new messages (milliseconds).
   /// </summary>
   public int BlockMs { get; set; } = 2000;

   /// <summary>
   ///    Approximate maximum length of the event stream.
   ///    <para>
   ///       Streams are append-only, so without a cap a long-running deployment grows memory
   ///       linearly with the number of debates ever run — completed ones included. Redis
   ///       trims with <c>MAXLEN ~</c> (approximate), which is O(1) and may exceed the cap by a
   ///       small margin rather than blocking the writer.
   ///    </para>
   ///    <para>
   ///       Applied by <c>RedisDebateOrchestrator</c> to the event stream. This repository only
   ///       reads <see cref="JobStreamKey" />, so whoever publishes to it must pass the same
   ///       cap. Set to 0 to disable trimming.
   ///    </para>
   /// </summary>
   public int StreamMaxLength { get; set; } = 10_000;

   /// <summary>
   ///    Lifetime of a debate state key (<c>{StateKeyPrefix}{debateId}</c>).
   ///    <para>
   ///       The TTL is refreshed on every state write, so a debate that is progressing stays
   ///       alive; a debate that stalled for longer than this is forgotten, which is the
   ///       intended behaviour rather than a leak.
   ///    </para>
   ///    <para>Set to <c>null</c> to keep state keys indefinitely.</para>
   /// </summary>
   public TimeSpan? StateKeyTtl { get; set; } = TimeSpan.FromHours(24);

   /// <summary>
   ///    Maximum number of messages to read per XREADGROUP call.
   /// </summary>
   public int BatchSize { get; set; } = 10;

   /// <summary>
   ///    Prefix for Redis hash keys storing debate state (status, result).
   /// </summary>
   public string StateKeyPrefix { get; set; } = BuiltIn.Redis.StateKeyPrefix;

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
