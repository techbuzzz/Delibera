using System.Text.Json;
using Delibera.Core.Caching;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Voting;
using Delibera.Redis.Serialization;
using Microsoft.Extensions.Logging;
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
    private readonly RedisOrchestratorOptions _options;
    private readonly TimeSpan _defaultTtl;
    private readonly ILogger<RedisDebateCache> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public RedisDebateCache(
        IConnectionMultiplexer redis,
        IOptions<RedisOrchestratorOptions> options,
        TimeSpan? defaultTtl = null,
        ILogger<RedisDebateCache>? logger = null)
    {
        _redis = redis;
        _options = options.Value;
        _defaultTtl = defaultTtl ?? TimeSpan.FromHours(24);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisDebateCache>.Instance;
        _db = redis.GetDatabase();
    }

    public async ValueTask<DebateResult?> GetAsync(string cacheKey, CancellationToken ct = default)
    {
        try
        {
            var redisKey = CacheKeyPrefix + cacheKey;
            var value = await _db.StringGetAsync(redisKey).ConfigureAwait(false);

            if (!value.HasValue)
            {
                _logger.LogDebug("Cache MISS for key {CacheKey} in Redis.", cacheKey);
                return null;
            }

            var redisResult = JsonSerializer.Deserialize<RedisDebateResult>((string)value, _json);
            if (redisResult is null)
                return null;

            _logger.LogDebug("Cache HIT for key {CacheKey} in Redis.", cacheKey);
            return MapFromRedis(redisResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cache key {CacheKey} from Redis.", cacheKey);
            return null;
        }
    }

    public async ValueTask SetAsync(string cacheKey, DebateResult result, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            var redisKey = CacheKeyPrefix + cacheKey;
            var effectiveTtl = ttl ?? _defaultTtl;
            var redisResult = result.ToRedisResult();
            var json = JsonSerializer.Serialize(redisResult, _json);

            await _db.StringSetAsync(redisKey, json, effectiveTtl).ConfigureAwait(false);
            _logger.LogDebug("Cache SET for key {CacheKey} in Redis with TTL {TTL}.", cacheKey, effectiveTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set cache key {CacheKey} in Redis.", cacheKey);
        }
    }

    public async ValueTask InvalidateAsync(string cacheKey, CancellationToken ct = default)
    {
        try
        {
            var redisKey = CacheKeyPrefix + cacheKey;
            await _db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            _logger.LogDebug("Cache INVALIDATE for key {CacheKey} in Redis.", cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache key {CacheKey} in Redis.", cacheKey);
        }
    }

    public async ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken ct = default)
    {
        try
        {
            var redisKey = CacheKeyPrefix + cacheKey;
            return await _db.KeyExistsAsync(redisKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check cache key {CacheKey} in Redis.", cacheKey);
            return false;
        }
    }

    private string CacheKeyPrefix => _options.StateKeyPrefix + "cache:";

    private static DebateResult MapFromRedis(RedisDebateResult redis) => new()
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
}