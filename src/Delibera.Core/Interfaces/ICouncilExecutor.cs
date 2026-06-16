using Delibera.Core.Council;

namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for executing a configured council debate session.
///    Enables dependency injection and testability of debate execution.
/// </summary>
public interface ICouncilExecutor
{
   /// <summary>Council participants.</summary>
   IReadOnlyList<CouncilMember> Members { get; }

   /// <summary>Chairman (may be <c>null</c>).</summary>
   CouncilMember? Chairman { get; }

   /// <summary>Knowledge Keeper (may be <c>null</c>).</summary>
   KnowledgeKeeper? KnowledgeKeeper { get; }

   /// <summary>Operator (may be <c>null</c>).</summary>
   Operator? Operator { get; }

   /// <summary>Debate strategy.</summary>
   IDebateStrategy Strategy { get; }

   /// <summary>Context compressor (may be <c>null</c> if compression is disabled).</summary>
   IContextCompressor? Compressor { get; }

   /// <summary>
   ///    Execution logs collected during the debate.
   ///    Empty until <see cref="ExecuteAsync" /> is called.
   /// </summary>
   IReadOnlyList<ExecutionLog> ExecutionLogs { get; }

   /// <summary>Invoked after each round completes.</summary>
   event Action<DebateRound>? OnRoundCompleted;

   /// <summary>
   ///    Runs the debate and returns the full result.
   /// </summary>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Complete debate result with rounds, verdict, logs, and metadata.</returns>
   Task<DebateResult> ExecuteAsync(CancellationToken ct = default);

   /// <summary>
   ///    Compresses text using the configured compressor, with optional caching.
   ///    Returns the original text unchanged if no compressor is configured.
   /// </summary>
   /// <param name="text">Text to compress.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Compressed context (or pass-through if no compressor).</returns>
   Task<CompressedContext> CompressTextAsync(string text, CancellationToken ct = default);

   /// <summary>
   ///    Returns a formatted summary of the council configuration.
   /// </summary>
   string GetInfo();
}