using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Delibera.Core.Compression;

/// <summary>
///    Thread-safe in-memory cache for compressed context results.
///    Avoids re-compressing identical or near-identical text during a debate session.
/// </summary>
/// <remarks>
///    <para>
///       Keys are derived from a SHA-256 hash of the input text plus the strategy name,
///       ensuring deterministic lookups even for large inputs.
///    </para>
///    <para>
///       The cache has a configurable maximum size and uses LRU-style eviction
///       when the limit is reached.
///    </para>
/// </remarks>
public sealed class CompressionCache(int maxEntries = 256)
{
   private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
   private readonly int _maxEntries = Math.Max(1, maxEntries);
   private long _hitCount;
   private long _missCount;

   /// <summary>Number of entries currently in the cache.</summary>
   public int Count => _cache.Count;

   /// <summary>Total cache hits since creation.</summary>
   public long HitCount => Interlocked.Read(ref _hitCount);

   /// <summary>Total cache misses since creation.</summary>
   public long MissCount => Interlocked.Read(ref _missCount);

   /// <summary>Cache hit rate (0.0–1.0).</summary>
   public double HitRate
   {
      get
      {
         var hits = HitCount;
         var misses = MissCount;
         var total = hits + misses;
         return total > 0
            ? (double)hits / total
            : 0.0;
      }
   }

   /// <summary>
   ///    Tries to retrieve a cached compression result.
   /// </summary>
   /// <param name="text">Original text.</param>
   /// <param name="strategyName">Compression strategy name.</param>
   /// <param name="result">Cached result if found.</param>
   /// <returns><c>true</c> if a cache hit occurred.</returns>
   public bool TryGet(string text, string strategyName, out CompressedContext? result)
   {
      var key = ComputeKey(text, strategyName);
      if (_cache.TryGetValue(key, out var entry))
      {
         entry.LastAccessed = DateTime.UtcNow;
         Interlocked.Increment(ref _hitCount);
         result = entry.Context;
         return true;
      }

      Interlocked.Increment(ref _missCount);
      result = null;
      return false;
   }

   /// <summary>
   ///    Stores a compression result in the cache.
   /// </summary>
   /// <param name="text">Original text (used to compute the cache key).</param>
   /// <param name="strategyName">Compression strategy name.</param>
   /// <param name="context">Compressed context to cache.</param>
   public void Set(string text, string strategyName, CompressedContext context)
   {
      var key = ComputeKey(text, strategyName);

      // Evict oldest entries if at capacity
      while (_cache.Count >= _maxEntries)
      {
         var oldest = _cache.OrderBy(kv => kv.Value.LastAccessed).FirstOrDefault();
         if (oldest.Key is not null)
            _cache.TryRemove(oldest.Key, out _);
         else
            break;
      }

      _cache[key] = new CacheEntry { Context = context, LastAccessed = DateTime.UtcNow };
   }

   /// <summary>Clears all cached entries and resets counters.</summary>
   public void Clear()
   {
      _cache.Clear();
      Interlocked.Exchange(ref _hitCount, 0);
      Interlocked.Exchange(ref _missCount, 0);
   }

   /// <summary>
   ///    Returns a formatted summary of cache performance.
   /// </summary>
   public string GetSummary() => $"Cache: {Count} entries, {HitCount} hits / {MissCount} misses ({HitRate:P1} hit rate)";

   // ──────────────────────────────────────────────

   private static string ComputeKey(string text, string strategyName)
   {
      var input = $"{strategyName}:{text}";
      var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
      return Convert.ToHexString(hashBytes);
   }

   private sealed class CacheEntry
   {
      public required CompressedContext Context { get; init; }
      public DateTime LastAccessed { get; set; }
   }
}
