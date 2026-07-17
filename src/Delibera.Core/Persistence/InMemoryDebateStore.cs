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
   public ValueTask<string> SaveCheckpointAsync(DebateCheckpoint checkpoint, CancellationToken ct = default)
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
      return new ValueTask<string>(id);
   }

   /// <inheritdoc />
   public ValueTask<DebateCheckpoint?> LoadCheckpointAsync(string debateId, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(debateId);
      ct.ThrowIfCancellationRequested();
      return new ValueTask<DebateCheckpoint?>(_checkpoints.TryGetValue(debateId, out var cp)
         ? cp
         : null);
   }

   /// <inheritdoc />
   public ValueTask<IReadOnlyList<DebateCheckpointMeta>> ListAsync(CancellationToken ct = default)
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
      return new ValueTask<IReadOnlyList<DebateCheckpointMeta>>(metas);
   }

   /// <inheritdoc />
   public ValueTask DeleteAsync(string debateId, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(debateId);
      _checkpoints.TryRemove(debateId, out _);
      return ValueTask.CompletedTask;
   }

   private static string Truncate(string s)
   {
      return string.IsNullOrEmpty(s) ? string.Empty : s.Length <= 80 ? s : s[..80] + "…";
   }
}
