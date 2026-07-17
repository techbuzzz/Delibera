using System.Collections.Concurrent;
using System.Threading.Channels;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Voting;
using Delibera.Redis.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Delibera.Redis;

/// <summary>
///    Distributed <see cref="IDebateOrchestrator" /> that uses Redis Streams for
///    job distribution and event broadcasting. Workers execute individual member turns
///    by consuming from the job stream; the orchestrator publishes round events and
///    final results back to Redis for SSE streaming.
///    <para>
///       In the current implementation, the debate is still executed locally
///       (each EnqueueAsync runs <see cref="ICouncilExecutor.ExecuteAsync" /> in-process),
///       but round events are published to Redis Streams so that any connected
///       API server can stream them via SSE. Full distributed turn execution
///       will be added in a future iteration.
///    </para>
/// </summary>
public sealed class RedisDebateOrchestrator : IDebateOrchestrator, IAsyncDisposable
{
   private readonly RedisOrchestratorOptions _options;
   private readonly IServiceProvider _services;
   private readonly ILogger<RedisDebateOrchestrator> _logger;
   private readonly ConnectionMultiplexer _redis;
   private readonly IDatabase _db;
   private readonly ConcurrentDictionary<string, DebateEntry> _entries = new();

   /// <summary>
   ///    Completed entries are evicted after this timeout to prevent unbounded memory growth.
   /// </summary>
   private static readonly TimeSpan CompletedEntryLifetime = TimeSpan.FromMinutes(30);

   private readonly Timer _evictionTimer;
   private bool _disposed;

   public RedisDebateOrchestrator(
      IOptions<RedisOrchestratorOptions> options,
      IServiceProvider services,
      ILogger<RedisDebateOrchestrator> logger)
   {
      _services = services;
      _logger = logger;
      _options = options.Value;
      _redis = ConnectionMultiplexer.Connect(_options.ConnectionString);
      _db = _redis.GetDatabase();

      _evictionTimer = new(EvictCompletedEntries, null, CompletedEntryLifetime, CompletedEntryLifetime);

      _ = EnsureConsumerGroupsAsync();
   }

   /// <inheritdoc />
   public async Task<DebateResult> ExecuteAsync(ICouncilBuilder builder, CancellationToken ct = default)
   {
      var executor = builder.Build();
      return await executor.ExecuteAsync(ct).ConfigureAwait(false);
   }

   /// <inheritdoc />
   public async Task<DebateHandle> EnqueueAsync(string debateId, ICouncilBuilder builder, CancellationToken ct = default)
   {
      var entry = new DebateEntry(builder);
      if (!_entries.TryAdd(debateId, entry))
         throw new InvalidOperationException($"Debate '{debateId}' already exists.");

      // Write initial state to Redis
      await SetStateAsync(debateId, DebateOrchestrationStatus.Running, cancellationToken: ct).ConfigureAwait(false);

      // Fire and forget — the task writes results into the entry and publishes to Redis.
      _ = RunDebateAsync(debateId, entry, ct);

      return new DebateHandle
      {
         DebateId = debateId,
         Status = DebateOrchestrationStatus.Running,
         CreatedAt = DateTimeOffset.UtcNow,
      };
   }

   /// <inheritdoc />
   public async ValueTask<DebateHandle?> GetStatusAsync(string debateId, CancellationToken ct = default)
   {
      // Check local cache first
      if (_entries.TryGetValue(debateId, out var localEntry))
         return BuildHandle(debateId, localEntry);

      // Fall back to Redis state
      var status = await ReadStateAsync(debateId, ct).ConfigureAwait(false);
      if (status is null)
         return null;

      return new DebateHandle
      {
         DebateId = debateId,
         Status = status.Value.status,
         Result = status.Value.result,
         ErrorMessage = status.Value.errorMessage,
      };
   }

   /// <inheritdoc />
   public async IAsyncEnumerable<DebateRoundEvent> StreamAsync(
      string debateId,
      [System.Runtime.CompilerServices.EnumeratorCancellation]
      CancellationToken ct = default)
   {
      if (!_entries.TryGetValue(debateId, out var entry))
         yield break;

      // Yield rounds that completed before the client connected.
      foreach (var round in entry.CompletedRounds)
         yield return new DebateRoundEvent.RoundCompleted(debateId, round);

      // If terminal, yield the terminal event and exit.
      if (entry.Status is DebateOrchestrationStatus.Completed)
      {
         yield return new DebateRoundEvent.DebateCompleted(debateId, entry.Result!);
         yield break;
      }

      if (entry.Status is DebateOrchestrationStatus.Failed)
      {
         yield return new DebateRoundEvent.DebateFailed(debateId, entry.ErrorMessage ?? "Unknown error");
         yield break;
      }

      if (entry.Status is DebateOrchestrationStatus.Cancelled)
      {
         yield return new DebateRoundEvent.DebateCancelled(debateId);
         yield break;
      }

      // Stream live rounds from the in-process channel.
      await foreach (var round in entry.Channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
         yield return new DebateRoundEvent.RoundCompleted(debateId, round);

      // After the channel completes, yield the terminal event.
      if (entry.Status is DebateOrchestrationStatus.Completed)
         yield return new DebateRoundEvent.DebateCompleted(debateId, entry.Result!);
      else if (entry.Status is DebateOrchestrationStatus.Failed)
         yield return new DebateRoundEvent.DebateFailed(debateId, entry.ErrorMessage ?? "Unknown error");
      else if (entry.Status is DebateOrchestrationStatus.Cancelled)
         yield return new DebateRoundEvent.DebateCancelled(debateId);
   }

   /// <inheritdoc />
   public async Task<bool> CancelAsync(string debateId, CancellationToken ct = default)
   {
      if (!_entries.TryGetValue(debateId, out var entry))
         return false;

      if (entry.Status is DebateOrchestrationStatus.Completed
          or DebateOrchestrationStatus.Failed
          or DebateOrchestrationStatus.Cancelled)
         return false;

      entry.Cancel();
      await SetStateAsync(debateId, DebateOrchestrationStatus.Cancelled, cancellationToken: ct).ConfigureAwait(false);
      return true;
   }

   // ── Private: debate execution ──────────────────────────────────────────────

   private async Task RunDebateAsync(string debateId, DebateEntry entry, CancellationToken outerCt)
   {
      try
      {
         var executor = entry.Builder.Build();
         executor.OnRoundCompleted += round =>
         {
            entry.CompletedRounds.Add(round);
            entry.Channel.Writer.TryWrite(round);

            // Publish to Redis stream for cross-instance SSE subscribers
            _ = PublishRoundEventAsync(debateId, round);
         };

         using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt, entry.Cts.Token);
         entry.Result = await executor.ExecuteAsync(linkedCts.Token).ConfigureAwait(false);
         entry.Status = DebateOrchestrationStatus.Completed;
         entry.CompletedAt = DateTimeOffset.UtcNow;

         await SetStateAsync(debateId, DebateOrchestrationStatus.Completed, entry.Result,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (entry.Cts.IsCancellationRequested)
      {
         entry.Status = DebateOrchestrationStatus.Cancelled;
         entry.CompletedAt = DateTimeOffset.UtcNow;
      }
      catch (Exception ex)
      {
         entry.Status = DebateOrchestrationStatus.Failed;
         entry.ErrorMessage = ex.Message;
         entry.CompletedAt = DateTimeOffset.UtcNow;

         _logger.LogError(ex, "Debate {DebateId} failed.", debateId);

         await SetStateAsync(debateId, DebateOrchestrationStatus.Failed,
            errorMessage: ex.Message).ConfigureAwait(false);
      }
      finally
      {
         entry.Channel.Writer.TryComplete();
      }
   }

   // ── Private: Redis operations ───────────────────────────────────────────────

   private async Task SetStateAsync(
      string debateId,
      DebateOrchestrationStatus status,
      DebateResult? result = null,
      string? errorMessage = null,
      CancellationToken cancellationToken = default)
   {
      try
      {
         var key = $"{_options.StateKeyPrefix}{debateId}";
         var hash = new List<HashEntry>
         {
            new("status", status.ToString()),
            new("updatedAt", DateTimeOffset.UtcNow.ToString("O")),
         };

         if (result is not null)
         {
            var redisResult = result.ToRedisResult();
            hash.Add(new HashEntry("result", RedisSerializer.Serialize(redisResult)));
         }

         if (errorMessage is not null)
            hash.Add(new HashEntry("errorMessage", errorMessage));

         await _db.HashSetAsync(key, [.. hash]).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to persist state to Redis for debate {DebateId}.", debateId);
      }
   }

   private async Task<(DebateOrchestrationStatus status, DebateResult? result, string? errorMessage)?> ReadStateAsync(
      string debateId, CancellationToken ct)
   {
      try
      {
         var key = $"{_options.StateKeyPrefix}{debateId}";
         var entries = await _db.HashGetAllAsync(key).ConfigureAwait(false);
         if (entries.Length == 0)
            return null;

         var statusStr = entries.FirstOrDefault(e => e.Name == "status").Value;
         var resultStr = entries.FirstOrDefault(e => e.Name == "result").Value;
         var errorStr = entries.FirstOrDefault(e => e.Name == "errorMessage").Value;

         if (!Enum.TryParse<DebateOrchestrationStatus>(statusStr, out var status))
            return null;

         DebateResult? result = null;
         if (resultStr.HasValue)
            result = MapFromRedis(RedisSerializer.Deserialize<RedisDebateResult>(resultStr!));

         return (status, result, errorStr.IsNullOrEmpty
            ? null
            : (string?)errorStr);
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to read Redis state for debate {DebateId}.", debateId);
         return null;
      }
   }

   private async Task PublishRoundEventAsync(string debateId, DebateRound round)
   {
      try
      {
         var redisEvent = round.ToRedisEvent(debateId);
         var json = RedisSerializer.Serialize(redisEvent);

         await _db.StreamAddAsync(_options.EventStreamKey,
         [
            new NameValueEntry("debateId", debateId),
            new NameValueEntry("eventType", "round-completed"),
            new NameValueEntry("payload", json),
         ]).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to publish round event to Redis for debate {DebateId}.", debateId);
      }
   }

   private async Task EnsureConsumerGroupsAsync()
   {
      async Task CreateGroupIfNotExists(string streamKey, string groupName)
      {
         try
         {
            await _db.StreamCreateConsumerGroupAsync(streamKey, groupName, "0-0").ConfigureAwait(false);
         }
         catch (RedisException ex) when (ex.Message.Contains("BUSYGROUP"))
         {
            // Consumer group already exists — that's fine.
         }
      }

      await CreateGroupIfNotExists(_options.EventStreamKey, _options.OrchestratorConsumerGroup).ConfigureAwait(false);
      await CreateGroupIfNotExists(_options.JobStreamKey, _options.WorkerConsumerGroup).ConfigureAwait(false);
   }

   // ── Eviction ───────────────────────────────────────────────────────────────

   private void EvictCompletedEntries(object? state)
   {
      var cutoff = DateTimeOffset.UtcNow - CompletedEntryLifetime;
      foreach (var kvp in _entries)
      {
         if (kvp.Value.Status is not DebateOrchestrationStatus.Running && kvp.Value.CompletedAt < cutoff)
         {
            _entries.TryRemove(kvp.Key, out _);
         }
      }
   }

   // ── Private: mapping ────────────────────────────────────────────────────────

   private static DebateHandle BuildHandle(string debateId, DebateEntry entry) => new()
   {
      DebateId = debateId,
      Status = entry.Status,
      Result = entry.Result,
      ErrorMessage = entry.ErrorMessage,
      CreatedAt = entry.CreatedAt,
      CompletedAt = entry.CompletedAt,
   };

   private static DebateResult MapFromRedis(RedisDebateResult? redis) => redis switch
   {
      null => throw new ArgumentNullException(nameof(redis)),
      _ => new DebateResult
      {
         DebateId = redis.DebateId,
         StrategyName = redis.StrategyName,
         Context = new PromptContext(),
         FinalVerdict = redis.FinalVerdict,
         ChairmanName = redis.ChairmanName,
         OpeningStatement = redis.OpeningStatement,
         StartedAt = redis.StartedAt,
         CompletedAt = redis.CompletedAt,
         Participants = redis.Rounds?.SelectMany(r => (r.Responses ?? []).Keys).Distinct().ToList() ?? [],
         Rounds = redis.Rounds?.Select(MapRound).ToList() ?? [],
         VotingTally = redis.WinningOption is not null
            ? new VotingResult(
               redis.WinningOption,
               redis.WinningScore ?? 0,
               redis.VotingTally?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, double>(),
               redis.VotingMethod ?? "unknown")
            : null,
      },
   };

   private static DebateRound MapRound(RedisRoundEvent r) => new()
   {
      RoundNumber = r.RoundNumber,
      RoundName = r.RoundName ?? string.Empty,
      Description = r.Description,
      Responses = r.Responses ?? new Dictionary<string, string>(),
      RoundPrompt = r.RoundPrompt,
      StartedAt = r.StartedAt,
      CompletedAt = r.CompletedAt,
   };

   // ── Private: entry ─────────────────────────────────────────────────────────

   private sealed class DebateEntry(ICouncilBuilder builder)
   {
      public ICouncilBuilder Builder { get; } = builder;
      private volatile int _status = (int)DebateOrchestrationStatus.Running;

      public DebateOrchestrationStatus Status
      {
#pragma warning disable CS0420 // Volatile.Read/Write on volatile field is intentional
         get => (DebateOrchestrationStatus)Volatile.Read(ref _status);
         set => Volatile.Write(ref _status, (int)value);
#pragma warning restore CS0420
      }

      public DebateResult? Result { get; set; }
      public string? ErrorMessage { get; set; }
      public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
      public DateTimeOffset? CompletedAt { get; set; }
      public List<DebateRound> CompletedRounds { get; } = [];
      public Channel<DebateRound> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<DebateRound>();
      public CancellationTokenSource Cts { get; } = new();

      public void Cancel()
      {
         Status = DebateOrchestrationStatus.Cancelled;
         Cts.Cancel();
      }
   }

   // ── IAsyncDisposable ───────────────────────────────────────────────────────

   public async ValueTask DisposeAsync()
   {
      if (_disposed) return;
      _disposed = true;

      _evictionTimer.Dispose();

      foreach (var entry in _entries.Values)
      {
         entry.Cts.Cancel();
         entry.Channel.Writer.TryComplete();
      }

      await _redis.DisposeAsync().ConfigureAwait(false);
   }
}
