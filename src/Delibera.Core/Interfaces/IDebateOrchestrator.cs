using Delibera.Core.Council;
using Delibera.Core.Models;

namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for coordinating debate execution.
///    <para>
///       The default implementation (<see cref="LocalDebateOrchestrator" />) runs the debate
///       in-process via <see cref="CouncilExecutor" />. Distributed implementations (e.g.
///       <c>RedisDebateOrchestrator</c>) distribute member turns across workers using a
///       message bus.
///    </para>
/// </summary>
public interface IDebateOrchestrator
{
   /// <summary>
   ///    Starts a debate and waits for completion.
   /// </summary>
   /// <param name="builder">A fully-configured <see cref="ICouncilBuilder" /> ready to call <c>Build()</c>.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The completed debate result.</returns>
   Task<DebateResult> ExecuteAsync(ICouncilBuilder builder, CancellationToken ct = default);

   /// <summary>
   ///    Starts a debate and returns immediately. Round events are published
   ///    via <see cref="StreamAsync" />.
   /// </summary>
   /// <param name="debateId">Unique identifier for the debate (for status tracking and event streaming).</param>
   /// <param name="builder">A fully-configured <see cref="ICouncilBuilder" />.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>A <see cref="DebateHandle" /> containing the debate ID and initial state.</returns>
   Task<DebateHandle> EnqueueAsync(string debateId, ICouncilBuilder builder, CancellationToken ct = default);

   /// <summary>
   ///    Gets the current status of a debate.
   /// </summary>
   /// <param name="debateId">The debate identifier returned by <see cref="EnqueueAsync" />.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The debate handle, or <c>null</c> if not found.</returns>
   Task<DebateHandle?> GetStatusAsync(string debateId, CancellationToken ct = default);

   /// <summary>
   ///    Streams debate round events as they occur.
   ///    Completes when the debate finishes (completed, failed, or cancelled).
   /// </summary>
   /// <param name="debateId">The debate identifier.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>An async enumerable of round events.</returns>
   IAsyncEnumerable<DebateRoundEvent> StreamAsync(string debateId, CancellationToken ct = default);

   /// <summary>
   ///    Cancels a running debate. Workers will stop processing turns for this debate.
   /// </summary>
   /// <param name="debateId">The debate identifier.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns><c>true</c> if the debate was found and cancelled; <c>false</c> if not found or already terminal.</returns>
   Task<bool> CancelAsync(string debateId, CancellationToken ct = default);
}

/// <summary>
///    Represents the state of a debate in the orchestration layer.
/// </summary>
public sealed class DebateHandle
{
   /// <summary>Unique identifier for the debate.</summary>
   public required string DebateId { get; init; }
   /// <summary>Current orchestration status of the debate.</summary>
   public required DebateOrchestrationStatus Status { get; init; }
   /// <summary>The completed debate result, or <c>null</c> if still running.</summary>
   public DebateResult? Result { get; init; }
   /// <summary>Error message when <see cref="Status" /> is <see cref="DebateOrchestrationStatus.Failed" />.</summary>
   public string? ErrorMessage { get; init; }
   /// <summary>When the debate was created.</summary>
   public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
   /// <summary>When the debate reached a terminal state, or <c>null</c> if still running.</summary>
   public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
///    Status of a debate in the orchestration layer.
/// </summary>
public enum DebateOrchestrationStatus
{
   /// <summary>The debate is currently executing rounds.</summary>
   Running,
   /// <summary>The debate completed successfully and produced a result.</summary>
   Completed,
   /// <summary>The debate failed with an error.</summary>
   Failed,
   /// <summary>The debate was cancelled by the user.</summary>
   Cancelled
}

/// <summary>
///    Event emitted during a debate — either a round completion or a terminal event.
/// </summary>
public abstract record DebateRoundEvent(string DebateId)
{
   /// <summary>A round completed event.</summary>
   public sealed record RoundCompleted(string DebateId, DebateRound Round) : DebateRoundEvent(DebateId);

   /// <summary>The debate finished successfully.</summary>
   public sealed record DebateCompleted(string DebateId, DebateResult Result) : DebateRoundEvent(DebateId);

   /// <summary>The debate failed with an error.</summary>
   public sealed record DebateFailed(string DebateId, string Error) : DebateRoundEvent(DebateId);

   /// <summary>The debate was cancelled.</summary>
   public sealed record DebateCancelled(string DebateId) : DebateRoundEvent(DebateId);
}