using System.Text.Json;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Microsoft.Extensions.Logging;

namespace Delibera.Core.Caching;

/// <summary>
///    File-system <see cref="IDebateCache" /> that persists <see cref="DebateResult" />
///    as JSON files in a configured directory. File name = <c>{cacheKey}.cache.json</c>.
///    TTL is enforced via file modification time.
///    Suitable for local development, CLI benchmark caching.
/// </summary>
public sealed class FileDebateCache : IDebateCache
{
   private readonly string _cacheDirectory;
   private readonly TimeSpan _defaultTtl;
   private readonly ILogger<FileDebateCache> _logger;

   private static readonly JsonSerializerOptions _json = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
      WriteIndented = false,
   };

   /// <summary>
   ///    Creates a new file-system cache.
   /// </summary>
   /// <param name="cacheDirectory">Directory where cache files are stored.</param>
   /// <param name="defaultTtl">Default TTL for cache entries. Defaults to 7 days.</param>
   /// <param name="logger">Optional logger.</param>
   public FileDebateCache(string cacheDirectory, TimeSpan? defaultTtl = null, ILogger<FileDebateCache>? logger = null)
   {
      _cacheDirectory = cacheDirectory;
      _defaultTtl = defaultTtl ?? TimeSpan.FromDays(7);
      _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileDebateCache>.Instance;

      if (!Directory.Exists(_cacheDirectory))
         Directory.CreateDirectory(_cacheDirectory);
   }

   /// <inheritdoc />
   public ValueTask<DebateResult?> GetAsync(string cacheKey, CancellationToken ct = default)
   {
      var filePath = GetFilePath(cacheKey);
      if (!File.Exists(filePath))
      {
         _logger.LogDebug("Cache MISS (file not found) for key {CacheKey}.", cacheKey);
         return new ValueTask<DebateResult?>((DebateResult?)null);
      }

      var fileInfo = new FileInfo(filePath);
      if (fileInfo.LastWriteTimeUtc + _defaultTtl < DateTime.UtcNow)
      {
         _logger.LogDebug("Cache MISS (expired) for key {CacheKey}.", cacheKey);
         try
         {
            File.Delete(filePath);
         }
         catch
         {
            /* best effort */
         }

         return new ValueTask<DebateResult?>((DebateResult?)null);
      }

      try
      {
         var json = File.ReadAllText(filePath);
         var result = JsonSerializer.Deserialize<DebateResult>(json, _json);
         _logger.LogDebug("Cache HIT for key {CacheKey}.", cacheKey);
         return new ValueTask<DebateResult?>(result);
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to deserialize cache file for key {CacheKey}.", cacheKey);
         return new ValueTask<DebateResult?>((DebateResult?)null);
      }
   }

   /// <inheritdoc />
   public ValueTask SetAsync(string cacheKey, DebateResult result, TimeSpan? ttl = null, CancellationToken ct = default)
   {
      var filePath = GetFilePath(cacheKey);
      try
      {
         var json = JsonSerializer.Serialize(result, _json);
         File.WriteAllText(filePath, json);
         _logger.LogDebug("Cache SET for key {CacheKey} at {Path}.", cacheKey, filePath);
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Failed to write cache file for key {CacheKey}.", cacheKey);
      }

      return ValueTask.CompletedTask;
   }

   /// <inheritdoc />
   public ValueTask InvalidateAsync(string cacheKey, CancellationToken ct = default)
   {
      var filePath = GetFilePath(cacheKey);
      if (File.Exists(filePath))
      {
         try
         {
            File.Delete(filePath);
         }
         catch
         {
            /* best effort */
         }

         _logger.LogDebug("Cache INVALIDATE for key {CacheKey}.", cacheKey);
      }

      return ValueTask.CompletedTask;
   }

   /// <inheritdoc />
   public ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken ct = default)
   {
      var filePath = GetFilePath(cacheKey);
      if (!File.Exists(filePath))
         return new ValueTask<bool>(false);

      var fileInfo = new FileInfo(filePath);
      if (fileInfo.LastWriteTimeUtc + _defaultTtl < DateTime.UtcNow)
      {
         try
         {
            File.Delete(filePath);
         }
         catch
         {
            /* best effort */
         }

         return new ValueTask<bool>(false);
      }

      return new ValueTask<bool>(true);
   }

   private string GetFilePath(string cacheKey) => Path.Combine(_cacheDirectory, $"{cacheKey}.cache.json");
}
