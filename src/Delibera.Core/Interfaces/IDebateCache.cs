using Delibera.Core.Models;

namespace Delibera.Core.Interfaces;

/// <summary>
///    Pluggable caching layer for <see cref="DebateResult" />.
///    Avoids re-running identical debates by storing results keyed by
///    a deterministic content hash.
/// </summary>
public interface IDebateCache
{
    /// <summary>
    ///    Retrieves a cached debate result, or <c>null</c> if not found or expired.
    /// </summary>
    Task<DebateResult?> GetAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    ///    Stores a debate result in the cache with an optional TTL.
    /// </summary>
    Task SetAsync(string cacheKey, DebateResult result, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    ///    Removes a cached result. Returns <c>true</c> if the key existed.
    /// </summary>
    Task InvalidateAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    ///    Checks whether a cache key exists (and is not expired).
    /// </summary>
    Task<bool> ExistsAsync(string cacheKey, CancellationToken ct = default);
}