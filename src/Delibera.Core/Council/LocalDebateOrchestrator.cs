using System.Collections.Concurrent;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;

namespace Delibera.Core.Council;

/// <summary>
///    In-process orchestrator that executes debates using <see cref="CouncilExecutor" />.
///    This is the default implementation — no external infrastructure required.
///    Round events are published via an in-process <see cref="Channel{T}" />.
/// </summary>
public sealed class LocalDebateOrchestrator : IDebateOrchestrator
{
   private readonly ConcurrentDictionary<string, DebateEntry> _entries = new();

   /// <inheritdoc />
   public async Task<DebateResult> ExecuteAsync(ICouncilBuilder builder, CancellationToken ct = default)
   {
      var executor = builder.Build();
      return await executor.ExecuteAsync(ct);
   }

   /// <inheritdoc />
   public Task<DebateHandle> EnqueueAsync(string debateId, ICouncilBuilder builder, CancellationToken ct = default)
   {
      var entry = new DebateEntry(builder);
      if (!_entries.TryAdd(debateId, entry))
         throw new InvalidOperationException($"Debate '{debateId}' already exists.");

      // Fire and forget — the task writes results into the entry.
      _ = RunDebateAsync(debateId, entry, ct);

      return Task.FromResult(new DebateHandle
      {
         DebateId = debateId,
         Status = DebateOrchestrationStatus.Running
      });
   }

   /// <inheritdoc />
   public Task<DebateHandle?> GetStatusAsync(string debateId, CancellationToken ct = default)
   {
      if (!_entries.TryGetValue(debateId, out var entry))
         return Task.FromResult<DebateHandle?>(null);

      var handle = new DebateHandle
      {
         DebateId = debateId,
         Status = entry.Status,
         Result = entry.Result,
         ErrorMessage = entry.ErrorMessage,
         CreatedAt = entry.CreatedAt,
         CompletedAt = entry.CompletedAt
      };
      return Task.FromResult<DebateHandle?>(handle);
   }

   /// <inheritdoc />
   public async IAsyncEnumerable<DebateRoundEvent> StreamAsync(
      string debateId,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
   {
      if (!_entries.TryGetValue(debateId, out var entry))
         yield break;

      // Yield rounds that already completed before the client connected.
      foreach (var round in entry.CompletedRounds)
         yield return new DebateRoundEvent.RoundCompleted(debateId, round);

      // If already terminal, yield the terminal event and exit.
      if (entry.Status is DebateOrchestrationStatus.Completed)
      {
         yield return new DebateRoundEvent.DebateCompleted(debateId, entry.Result!);
         yield break;
      }

      if (entry.Status is DebateOrchestrationStatus.Failed)
      {
         yield return new DebateRoundEvent.DebateFailed(debateId, entry.ErrorMessage ?? "Unknown error");
         yield break;
      }

      if (entry.Status is DebateOrchestrationStatus.Cancelled)
      {
         yield return new DebateRoundEvent.DebateCancelled(debateId);
         yield break;
      }

      // Stream live rounds from the channel.
      await foreach (var round in entry.Channel.Reader.ReadAllAsync(ct))
         yield return new DebateRoundEvent.RoundCompleted(debateId, round);

      // After the channel completes, yield the terminal event.
      if (entry.Status is DebateOrchestrationStatus.Completed)
         yield return new DebateRoundEvent.DebateCompleted(debateId, entry.Result!);
      else if (entry.Status is DebateOrchestrationStatus.Failed)
         yield return new DebateRoundEvent.DebateFailed(debateId, entry.ErrorMessage ?? "Unknown error");
      else if (entry.Status is DebateOrchestrationStatus.Cancelled)
         yield return new DebateRoundEvent.DebateCancelled(debateId);
   }

   /// <inheritdoc />
   public Task<bool> CancelAsync(string debateId, CancellationToken ct = default)
   {
      if (!_entries.TryGetValue(debateId, out var entry))
         return Task.FromResult(false);

      if (entry.Status is DebateOrchestrationStatus.Completed
                       or DebateOrchestrationStatus.Failed
                       or DebateOrchestrationStatus.Cancelled)
         return Task.FromResult(false);

      entry.Cancel();
      return Task.FromResult(true);
   }

   private static async Task RunDebateAsync(string debateId, DebateEntry entry, CancellationToken outerCt)
   {
      try
      {
         var executor = entry.Builder.Build();
         executor.OnRoundCompleted += round =>
         {
            entry.CompletedRounds.Add(round);
            entry.Channel.Writer.TryWrite(round);
         };

         using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt, entry.Cts.Token);
         entry.Result = await executor.ExecuteAsync(linkedCts.Token);
         entry.Status = DebateOrchestrationStatus.Completed;
         entry.CompletedAt = DateTimeOffset.UtcNow;
      }
      catch (OperationCanceledException) when (entry.Cts.IsCancellationRequested)
      {
         entry.Status = DebateOrchestrationStatus.Cancelled;
         entry.CompletedAt = DateTimeOffset.UtcNow;
      }
      catch (Exception ex)
      {
         entry.Status = DebateOrchestrationStatus.Failed;
         entry.ErrorMessage = ex.Message;
         entry.CompletedAt = DateTimeOffset.UtcNow;
      }
      finally
      {
         entry.Channel.Writer.TryComplete();
      }
   }

   private sealed class DebateEntry(ICouncilBuilder builder)
   {
      public ICouncilBuilder Builder { get; } = builder;
      public DebateOrchestrationStatus Status { get; set; } = DebateOrchestrationStatus.Running;
      public DebateResult? Result { get; set; }
      public string? ErrorMessage { get; set; }
      public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
      public DateTimeOffset? CompletedAt { get; set; }
      public List<DebateRound> CompletedRounds { get; } = [];
      public System.Threading.Channels.Channel<DebateRound> Channel { get; } =
         System.Threading.Channels.Channel.CreateUnbounded<DebateRound>();
      public CancellationTokenSource Cts { get; } = new();

      public void Cancel()
      {
         Status = DebateOrchestrationStatus.Cancelled;
         Cts.Cancel();
      }
   }
}