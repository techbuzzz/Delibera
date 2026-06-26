using System.Buffers;
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
///    <para>
///       Internally, entries are stored in a <see cref="ConcurrentDictionary{TKey,TValue}" />
///       for lock-free reads and a linked list for fast LRU ordering. Eviction scans
///       only when the capacity is exceeded and removes entries in batches of 16 to
///       reduce contention compared to the previous <c>OrderBy</c> implementation.
///    </para>
/// </remarks>
public sealed class CompressionCache(int maxEntries = 256)
{
   // Keep one slot free so we can add first, then evict, avoiding a write lock on reads.
   private readonly int _maxEntries = Math.Max(1, maxEntries);
   private readonly int _evictionTarget = Math.Max(1, (int)Math.Ceiling(Math.Max(1, maxEntries) * 0.0625)); // ~6.25% each time
   private readonly ConcurrentDictionary<string, LruNode> _cache = new();
   private readonly ReaderWriterLockSlim _lruLock = new();
   private LruNode? _head; // most recently used
   private LruNode? _tail; // least recently used
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
      if (_cache.TryGetValue(key, out var node))
      {
         Touch(node);
         Interlocked.Increment(ref _hitCount);
         result = node.Context;
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

      _lruLock.EnterUpgradeableReadLock();
      try
      {
         if (_cache.TryGetValue(key, out var existing))
         {
            existing.Context = context;
            Touch(existing);
            return;
         }

         // Add first, then evict if over capacity. This keeps reads lock-free.
         var node = new LruNode(key, context);
         _cache.TryAdd(key, node);

         _lruLock.EnterWriteLock();
         try
         {
            AddToHead(node);

            if (_cache.Count > _maxEntries)
               EvictOldest(_evictionTarget);
         }
         finally
         {
            _lruLock.ExitWriteLock();
         }
      }
      finally
      {
         _lruLock.ExitUpgradeableReadLock();
      }
   }

   /// <summary>Clears all cached entries and resets counters.</summary>
   public void Clear()
   {
      _lruLock.EnterWriteLock();
      try
      {
         _cache.Clear();
         _head = null;
         _tail = null;
      }
      finally
      {
         _lruLock.ExitWriteLock();
      }

      Interlocked.Exchange(ref _hitCount, 0);
      Interlocked.Exchange(ref _missCount, 0);
   }

   /// <summary>
   ///    Returns a formatted summary of cache performance.
   /// </summary>
   public string GetSummary()
   {
      return $"Cache: {Count} entries, {HitCount} hits / {MissCount} misses ({HitRate:P1} hit rate)";
   }

   // ──────────────────────────────────────────────

   private static string ComputeKey(string text, string strategyName)
   {
      // Compose "{strategy}:{text}" into a pooled char buffer, then UTF-8 encode
      // into a pooled byte buffer — avoids two intermediate heap allocations
      // (the interpolated string and the GetBytes array) on this hot path.
      var charCount = strategyName.Length + 1 + text.Length;
      var chars = ArrayPool<char>.Shared.Rent(charCount);
      byte[]? bytes = null;
      try
      {
         strategyName.AsSpan().CopyTo(chars);
         chars[strategyName.Length] = ':';
         text.AsSpan().CopyTo(chars.AsSpan(strategyName.Length + 1));
         var charSpan = chars.AsSpan(0, charCount);

         var maxBytes = Encoding.UTF8.GetMaxByteCount(charCount);
         bytes = ArrayPool<byte>.Shared.Rent(maxBytes);
         var byteCount = Encoding.UTF8.GetBytes(charSpan, bytes);

         Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
         SHA256.HashData(bytes.AsSpan(0, byteCount), hash);
         return Convert.ToHexString(hash);
      }
      finally
      {
         ArrayPool<char>.Shared.Return(chars);
         if (bytes is not null)
            ArrayPool<byte>.Shared.Return(bytes);
      }
   }

   private void Touch(LruNode node)
   {
      // If already at the head, nothing to do.
      if (_head == node)
         return;

      _lruLock.EnterWriteLock();
      try
      {
         if (_head == node || node.ListVersion != Volatile.Read(ref _listVersion))
            return; // node has been evicted since read

         RemoveNode(node);
         AddToHead(node);
      }
      finally
      {
         _lruLock.ExitWriteLock();
      }
   }

   private long _listVersion;

   private void AddToHead(LruNode node)
   {
      node.Next = _head;
      node.Previous = null;
      if (_head is not null)
         _head.Previous = node;

      _head = node;
      _tail ??= node;
      node.ListVersion = Interlocked.Increment(ref _listVersion);
   }

   private void RemoveNode(LruNode node)
   {
      if (node.Previous is not null)
         node.Previous.Next = node.Next;
      else
         _head = node.Next;

      if (node.Next is not null)
         node.Next.Previous = node.Previous;
      else
         _tail = node.Previous;

      node.Next = null;
      node.Previous = null;
   }

   private void EvictOldest(int count)
   {
      var removed = 0;
      while (_tail is not null && removed < count)
      {
         var key = _tail.Key;
         var previous = _tail.Previous;

         RemoveNode(_tail);
         _cache.TryRemove(key, out _);

         _tail = previous;
         removed++;
      }
   }

   private sealed class LruNode
   {
      public LruNode(string key, CompressedContext context)
      {
         Key = key;
         Context = context;
      }

      public string Key { get; }
      public CompressedContext Context { get; set; }
      public long ListVersion { get; set; }
      public LruNode? Next { get; set; }
      public LruNode? Previous { get; set; }
   }
}
