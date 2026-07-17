using System.Collections.Concurrent;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Delibera.Core.Caching;

/// <summary>
///    In-memory <see cref="IDebateCache" /> using <see cref="IMemoryCache" />.
///    Suitable for single-instance development and testing.
/// </summary>
public sealed class InMemoryDebateCache : IDebateCache
{
   private readonly IMemoryCache _cache;
   private readonly TimeSpan _defaultTtl;
   private readonly ILogger<InMemoryDebateCache> _logger;

   /// <summary>
   ///    Creates a new in-memory cache.
   /// </summary>
   /// <param name="cache">The underlying memory cache.</param>
   /// <param name="defaultTtl">Default TTL for cache entries. Defaults to 1 hour.</param>
   /// <param name="logger">Optional logger.</param>
   public InMemoryDebateCache(IMemoryCache cache, TimeSpan? defaultTtl = null, ILogger<InMemoryDebateCache>? logger = null)
   {
      _cache = cache;
      _defaultTtl = defaultTtl ?? TimeSpan.FromHours(1);
      _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InMemoryDebateCache>.Instance;
   }

   /// <inheritdoc />
   public ValueTask<DebateResult?> GetAsync(string cacheKey, CancellationToken ct = default)
   {
      _cache.TryGetValue<DebateResult>(cacheKey, out var result);
      if (result is not null)
         _logger.LogDebug("Cache HIT for key {CacheKey}.", cacheKey);
      else
         _logger.LogDebug("Cache MISS for key {CacheKey}.", cacheKey);

      return new ValueTask<DebateResult?>(result);
   }

   /// <inheritdoc />
   public ValueTask SetAsync(string cacheKey, DebateResult result, TimeSpan? ttl = null, CancellationToken ct = default)
   {
      var effectiveTtl = ttl ?? _defaultTtl;
      var options = new MemoryCacheEntryOptions
      {
         AbsoluteExpirationRelativeToNow = effectiveTtl,
         Size = 1,
      };

      _cache.Set(cacheKey, result, options);
      _logger.LogDebug("Cache SET for key {CacheKey} with TTL {TTL}.", cacheKey, effectiveTtl);
      return ValueTask.CompletedTask;
   }

   /// <inheritdoc />
   public ValueTask InvalidateAsync(string cacheKey, CancellationToken ct = default)
   {
      _cache.Remove(cacheKey);
      _logger.LogDebug("Cache INVALIDATE for key {CacheKey}.", cacheKey);
      return ValueTask.CompletedTask;
   }

   /// <inheritdoc />
   public ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken ct = default)
   {
      return new ValueTask<bool>(_cache.TryGetValue(cacheKey, out _));
   }
}
