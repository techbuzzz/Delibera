using System.Collections.Concurrent;

namespace Delibera.Server.Services;

/// <summary>
///    In-memory runtime record for a single debate lifecycle.
///    Holds status, result, and metadata. Round streaming is handled
///    via <see cref="IDebateOrchestrator.StreamAsync" />, not this record.
/// </summary>
public sealed class DebateRecord
{
   private volatile int _status;

   public required string DebateId { get; init; }
   public required string TemplateId { get; init; }
   public required string TenantId { get; init; }

   public DebateStatus Status
   {
      get => (DebateStatus)_status;
      set => _status = (int)value;
   }

   public DebateResult? Result { get; set; }

   /// <summary>
   ///    Rounds completed so far, in order.
   /// </summary>
   /// <remarks>
   ///    A <see cref="ConcurrentQueue{T}" /> rather than a <c>List&lt;T&gt;</c>: the debate
   ///    background task appends here while request threads read it for
   ///    <c>GET /debates/{id}/rounds</c>, the SSE writer and the MCP tools. Enumerating a
   ///    list under concurrent writes throws "Collection was modified" — non-deterministically,
   ///    and only under load.
   /// </remarks>
   public ConcurrentQueue<DebateRound> Rounds { get; } = new();

   public string? ErrorMessage { get; set; }

   /// <summary>
   ///    When the debate started. Settable because the synchronous endpoints finish before the
   ///    record is constructed, so the object initialiser would otherwise stamp it at completion
   ///    and make it indistinguishable from <see cref="CompletedAt" />.
   /// </summary>
   public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

   public DateTimeOffset? CompletedAt { get; set; }
   public string Label { get; set; } = string.Empty;

   /// <summary>
   ///    A stable copy of <see cref="Rounds" /> for reading, paginating and serialising.
   ///    Readers should use this rather than enumerating <see cref="Rounds" /> directly so
   ///    that a page is not taken from a collection that is still growing.
   /// </summary>
   public DebateRound[] RoundsSnapshot()
   {
      return Rounds.ToArray();
   }

   /// <summary>Records every round of a completed result, preserving order.</summary>
   public void AddRounds(IEnumerable<DebateRound> rounds)
   {
      foreach (var round in rounds)
         Rounds.Enqueue(round);
   }
}
