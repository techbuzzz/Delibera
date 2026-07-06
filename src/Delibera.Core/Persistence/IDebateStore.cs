namespace Delibera.Core.Persistence;

/// <summary>
///    Persists <see cref="DebateCheckpoint" />s so debates can be resumed after
///    crashes, restarts, or intentional pauses. Implementations include
///    <see cref="FileDebateStore" /> (atomic JSON write-then-rename) and
///    <see cref="InMemoryDebateStore" /> (testing / ephemeral).
/// </summary>
/// <remarks>
///    Implementations must be thread-safe — the executor may call
///    <see cref="SaveCheckpointAsync" /> after each round while the consumer
///    concurrently reads prior checkpoints via <see cref="ListAsync" />.
/// </remarks>
public interface IDebateStore
{
   /// <summary>
   ///    Saves the checkpoint and returns the debate identifier used to store it.
   ///    If <paramref name="checkpoint" />'s <see cref="DebateCheckpoint.DebateId" /> is
   ///    null or empty, the store generates a new ULID-style identifier and stamps
   ///    it on the checkpoint.
   /// </summary>
   /// <param name="checkpoint">The checkpoint to persist.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The debate identifier used for storage.</returns>
   Task<string> SaveCheckpointAsync(DebateCheckpoint checkpoint, CancellationToken ct = default);

   /// <summary>
   ///    Loads the checkpoint for the given <paramref name="debateId" />, or
   ///    <c>null</c> when no checkpoint exists.
   /// </summary>
   /// <param name="debateId">The debate identifier.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The loaded checkpoint, or <c>null</c>.</returns>
   Task<DebateCheckpoint?> LoadCheckpointAsync(string debateId, CancellationToken ct = default);

   /// <summary>
   ///    Lists metadata for every checkpoint currently stored, ordered by
   ///    <see cref="DebateCheckpointMeta.CreatedAt" /> descending (newest first).
   /// </summary>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Read-only list of checkpoint metadata.</returns>
   Task<IReadOnlyList<DebateCheckpointMeta>> ListAsync(CancellationToken ct = default);

   /// <summary>
   ///    Deletes the checkpoint for the given <paramref name="debateId" />.
   ///    No-op when no checkpoint exists.
   /// </summary>
   /// <param name="debateId">The debate identifier.</param>
   /// <param name="ct">Cancellation token.</param>
   Task DeleteAsync(string debateId, CancellationToken ct = default);
}
