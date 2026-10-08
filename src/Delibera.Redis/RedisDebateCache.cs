using System.Text.Json;
using System.Text.Json.Serialization;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Voting;
using Delibera.Redis.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Delibera.Redis;

/// <summary>
///    Redis-backed <see cref="IDebateCache" /> using <c>SETEX</c> with optional TTL.
///    Shares the Redis connection with <see cref="RedisDebateOrchestrator" />.
///    Suitable for distributed/production deployments.
/// </summary>
public sealed class RedisDebateCache : IDebateCache
{
   private static readonly JsonSerializerOptions _json = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
   };

   private readonly IDatabase _db;
   private readonly TimeSpan _defaultTtl;
   private readonly ILogger<RedisDebateCache> _logger;
   private readonly RedisOrchestratorOptions _options;
   private readonly IConnectionMultiplexer _redis;

   public RedisDebateCache(
      IConnectionMultiplexer redis,
      IOptions<RedisOrchestratorOptions> options,
      TimeSpan? defaultTtl = null,
      ILogger<RedisDebateCache>? logger = null)
   {
      _redis = redis;
      _options = options.Value;
      _defaultTtl = defaultTtl ?? TimeSpan.FromHours(24);
      _logger = logger ?? NullLogger<RedisDebateCache>.Instance;
      _db = redis.GetDatabase();
   }

   private string CacheKeyPrefix => _options.StateKeyPrefix + "cache:";

   public async ValueTask<DebateResult?> GetAsync(string cacheKey, CancellationToken ct = default)
   {
      // StackExchange.Redis exposes no CancellationToken overloads on IDatabase, so the
      // token cannot be forwarded into the command itself. It is honoured on both edges of
      // the I/O instead: do not start work for an already-cancelled caller, and stop
      // waiting for a result the caller no longer wants.
      ct.ThrowIfCancellationRequested();
      try
      {
         var redisKey = CacheKeyPrefix + cacheKey;
         var value = await _db.StringGetAsync(redisKey).ConfigureAwait(false);

         if (!value.HasValue)
         {
            _logger.LogDebug("Cache MISS for key {CacheKey} in Redis.", cacheKey);
            return null;
         }

         ct.ThrowIfCancellationRequested();

         // value.ToString() rather than (string)value: the explicit conversion is annotated
         // as possibly-null, which is true in general but not here (HasValue was checked).
         var redisResult = JsonSerializer.Deserialize<RedisDebateResult>(value.ToString(), _json);
         if (redisResult is null)
            return null;

         _logger.LogDebug("Cache HIT for key {CacheKey} in Redis.", cacheKey);
         return MapFromRedis(redisResult);
      }
      catch (OperationCanceledException)
      {
         // A cancelled caller is not a cache failure.
         throw;
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to get cache key {CacheKey} from Redis.", cacheKey);
         return null;
      }
   }

   public async ValueTask SetAsync(string cacheKey, DebateResult result, TimeSpan? ttl = null,
      CancellationToken ct = default)
   {
      ct.ThrowIfCancellationRequested();
      try
      {
         var redisKey = CacheKeyPrefix + cacheKey;
         var effectiveTtl = ttl ?? _defaultTtl;
         var redisResult = result.ToRedisResult();
         var json = JsonSerializer.Serialize(redisResult, _json);

         await _db.StringSetAsync(redisKey, json, effectiveTtl).ConfigureAwait(false);
         _logger.LogDebug("Cache SET for key {CacheKey} in Redis with TTL {TTL}.", cacheKey, effectiveTtl);
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to set cache key {CacheKey} in Redis.", cacheKey);
      }
   }

   public async ValueTask InvalidateAsync(string cacheKey, CancellationToken ct = default)
   {
      ct.ThrowIfCancellationRequested();
      try
      {
         var redisKey = CacheKeyPrefix + cacheKey;
         await _db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
         _logger.LogDebug("Cache INVALIDATE for key {CacheKey} in Redis.", cacheKey);
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to invalidate cache key {CacheKey} in Redis.", cacheKey);
      }
   }

   public async ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken ct = default)
   {
      ct.ThrowIfCancellationRequested();
      try
      {
         var redisKey = CacheKeyPrefix + cacheKey;
         var exists = await _db.KeyExistsAsync(redisKey).ConfigureAwait(false);
         ct.ThrowIfCancellationRequested();
         return exists;
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to check cache key {CacheKey} in Redis.", cacheKey);
         return false;
      }
   }

   private static DebateResult MapFromRedis(RedisDebateResult redis)
   {
      return new DebateResult
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
            : null
      };
   }

   private static DebateRound MapRound(RedisRoundEvent r)
   {
      return new DebateRound
      {
         RoundNumber = r.RoundNumber,
         RoundName = r.RoundName ?? string.Empty,
         Description = r.Description,
         Responses = r.Responses ?? new Dictionary<string, string>(),
         RoundPrompt = r.RoundPrompt,
         StartedAt = r.StartedAt,
         CompletedAt = r.CompletedAt
      };
   }
}
