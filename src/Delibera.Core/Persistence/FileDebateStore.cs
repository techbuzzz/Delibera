using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delibera.Core.Persistence;

/// <summary>
///    File-system implementation of <see cref="IDebateStore" /> that persists each
///    checkpoint as a JSON file in a configurable directory. Writes are atomic
///    (write-temp → rename) so a crash mid-write cannot leave a corrupt checkpoint.
/// </summary>
/// <remarks>
///    <para>
///       File naming: <c>{debateId}.checkpoint.json</c> in the configured directory.
///       Each checkpoint is a single self-contained JSON file, so the store can
///       support many concurrent debates without a single-file bottleneck.
///    </para>
///    <para>
///       <b>Retention</b>: optionally delete checkpoints older than
///       the configured retention period. The retention sweep runs lazily on
///       each <see cref="ListAsync" /> call (cheap; only reads file metadata).
///    </para>
/// </remarks>
public sealed class FileDebateStore : IDebateStore
{
   private static readonly JsonSerializerOptions JsonOptions = new()
   {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
   };

   private readonly string _directory;
   private readonly int? _retentionDays;
   private readonly SemaphoreSlim _writeLock = new(1, 1);

   /// <summary>
   ///    Creates a file-based store rooted at <paramref name="directory" />.
   ///    The directory is created if it does not exist.
   /// </summary>
   /// <param name="directory">Absolute or relative path to the checkpoint directory.</param>
   /// <param name="retentionDays">
   ///    Optional retention period in days. When set, older checkpoints are deleted lazily on
   ///    <see cref="ListAsync" />.
   /// </param>
   public FileDebateStore(string directory, int? retentionDays = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(directory);
      _directory = directory;
      _retentionDays = retentionDays;
      Directory.CreateDirectory(_directory);
   }

   /// <inheritdoc />
   public async Task<string> SaveCheckpointAsync(DebateCheckpoint checkpoint, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(checkpoint);
      ct.ThrowIfCancellationRequested();

      var id = string.IsNullOrEmpty(checkpoint.DebateId)
         ? DebateCheckpoint.GenerateId()
         : checkpoint.DebateId;
      var stamped = checkpoint with
      {
         DebateId = id, CreatedAt = checkpoint.CreatedAt == default
            ? DateTimeOffset.UtcNow
            : checkpoint.CreatedAt
      };

      var finalPath = GetFilePath(id);
      var tempPath = finalPath + ".tmp";

      await _writeLock.WaitAsync(ct).ConfigureAwait(false);
      try
      {
         await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
         {
            await JsonSerializer.SerializeAsync(stream, stamped, JsonOptions, ct).ConfigureAwait(false);
         }

         // Atomic rename — on Windows, File.Move with overwrite is atomic on NTFS.
         File.Move(tempPath, finalPath, true);
      }
      catch
      {
         // Clean up the temp file on failure.
         if (File.Exists(tempPath))
            try
            {
               File.Delete(tempPath);
            }
            catch
            {
               /* best-effort */
            }

         throw;
      }
      finally
      {
         _writeLock.Release();
      }

      return id;
   }

   /// <inheritdoc />
   public async Task<DebateCheckpoint?> LoadCheckpointAsync(string debateId, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(debateId);
      var path = GetFilePath(debateId);
      if (!File.Exists(path)) return null;
      await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
      return await JsonSerializer.DeserializeAsync<DebateCheckpoint>(stream, JsonOptions, ct).ConfigureAwait(false);
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<DebateCheckpointMeta>> ListAsync(CancellationToken ct = default)
   {
      if (!Directory.Exists(_directory)) return [];

      // Lazy retention sweep — delete checkpoints older than the retention period.
      if (_retentionDays is { } days)
      {
         var cutoff = DateTime.UtcNow.AddDays(-days);
         foreach (var file in Directory.EnumerateFiles(_directory, "*.checkpoint.json"))
            try
            {
               if (File.GetLastWriteTimeUtc(file) < cutoff)
                  File.Delete(file);
            }
            catch
            {
               /* best-effort; file may be locked */
            }
      }

      var metas = new List<DebateCheckpointMeta>();
      foreach (var file in Directory.EnumerateFiles(_directory, "*.checkpoint.json"))
      {
         ct.ThrowIfCancellationRequested();
         var id = Path.GetFileName(file).Replace(".checkpoint.json", "", StringComparison.OrdinalIgnoreCase);
         try
         {
            var checkpoint = await LoadCheckpointAsync(id, ct).ConfigureAwait(false);
            if (checkpoint is not null)
               metas.Add(new DebateCheckpointMeta(
                  checkpoint.DebateId,
                  checkpoint.CreatedAt,
                  checkpoint.LastCompletedRound,
                  TruncateForList(checkpoint.OriginalQuestion)));
         }
         catch
         {
            /* skip corrupt checkpoints */
         }
      }

      return metas.OrderByDescending(m => m.CreatedAt).ToList();
   }

   /// <inheritdoc />
   public Task DeleteAsync(string debateId, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(debateId);
      var path = GetFilePath(debateId);
      if (File.Exists(path)) File.Delete(path);
      return Task.CompletedTask;
   }

   private string GetFilePath(string id)
   {
      return Path.Combine(_directory, $"{id}.checkpoint.json");
   }

   private static string TruncateForList(string s)
   {
      return string.IsNullOrEmpty(s) ? string.Empty : s.Length <= 80 ? s : s[..80] + "…";
   }
}
