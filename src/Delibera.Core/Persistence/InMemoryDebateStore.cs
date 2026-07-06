using System.Collections.Concurrent;

namespace Delibera.Core.Persistence;

/// <summary>
///    In-memory implementation of <see cref="IDebateStore" /> for testing and
///    ephemeral use cases. Checkpoints live in a thread-safe dictionary and are
///    lost when the process exits.
/// </summary>
public sealed class InMemoryDebateStore : IDebateStore
{
   private readonly ConcurrentDictionary<string, DebateCheckpoint> _checkpoints = new();

   /// <inheritdoc />
   public Task<string> SaveCheckpointAsync(DebateCheckpoint checkpoint, CancellationToken ct = default)
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
      _checkpoints[id] = stamped;
      return Task.FromResult(id);
   }

   /// <inheritdoc />
   public Task<DebateCheckpoint?> LoadCheckpointAsync(string debateId, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(debateId);
      ct.ThrowIfCancellationRequested();
      return Task.FromResult(_checkpoints.TryGetValue(debateId, out var cp)
         ? cp
         : null);
   }

   /// <inheritdoc />
   public Task<IReadOnlyList<DebateCheckpointMeta>> ListAsync(CancellationToken ct = default)
   {
      ct.ThrowIfCancellationRequested();
      IReadOnlyList<DebateCheckpointMeta> metas = _checkpoints.Values
         .Select(cp => new DebateCheckpointMeta(
            cp.DebateId,
            cp.CreatedAt,
            cp.LastCompletedRound,
            Truncate(cp.OriginalQuestion)))
         .OrderByDescending(m => m.CreatedAt)
         .ToList();
      return Task.FromResult(metas);
   }

   /// <inheritdoc />
   public Task DeleteAsync(string debateId, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(debateId);
      _checkpoints.TryRemove(debateId, out _);
      return Task.CompletedTask;
   }

   private static string Truncate(string s)
   {
      return string.IsNullOrEmpty(s) ? string.Empty : s.Length <= 80 ? s : s[..80] + "…";
   }
}
