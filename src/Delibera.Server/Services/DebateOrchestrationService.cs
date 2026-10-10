using System.Collections.Concurrent;
using Delibera.Core;
using Delibera.Core.Interfaces;
using Delibera.Server.Scenarios;

namespace Delibera.Server.Services;

/// <summary>
///    HTTP-oriented orchestrator that bridges <see cref="CreateDebateRequest" /> /
///    <see cref="ScenarioRequest" /> to <see cref="ICouncilBuilder" /> and delegates
///    execution to <see cref="IDebateOrchestrator" />.
///    <para>
///       Completed debate records are automatically evicted after
///       <see cref="BuiltIn.Timeouts.CompletedDebateLifetime" /> to prevent unbounded memory growth.
///    </para>
/// </summary>
public sealed class DebateOrchestrationService : IDebateOrchestrationService, IDisposable
{
    private readonly TimeSpan _completedRecordLifetime;
    private readonly IConfiguration _configuration;
    private readonly Timer _evictionTimer;
    private readonly ILogger<DebateOrchestrationService> _logger;
    private readonly IDebateOrchestrator _orchestrator;
 
    private readonly ConcurrentDictionary<string, DebateRecord> _records = new();
    private readonly IServiceProvider _services;
    private readonly ITemplateRegistry _templates;
 
    public DebateOrchestrationService(
       ILogger<DebateOrchestrationService> logger,
       ITemplateRegistry templates,
       IServiceProvider services,
       IConfiguration configuration,
       IDebateOrchestrator orchestrator,
       TimeSpan? completedRecordLifetime = null)
    {
       _logger = logger;
       _templates = templates;
       _services = services;
       _configuration = configuration;
       _orchestrator = orchestrator;
       _completedRecordLifetime = completedRecordLifetime ?? BuiltIn.Timeouts.CompletedDebateLifetime;
      _evictionTimer = new Timer(EvictCompletedRecords, null, _completedRecordLifetime, _completedRecordLifetime);
   }

   // ── Template path ─────────────────────────────────────────────────────────

   public async Task<DebateRecord> RunAsync(
      CreateDebateRequest request,
      string tenantId,
      CancellationToken ct = default)
   {
      var builder = ResolveTemplateBuilder(request);
      var startedAt = DateTimeOffset.UtcNow;
      var result = await _orchestrator.ExecuteAsync(builder, ct).ConfigureAwait(false);
      var record = CreateRecord(request.TemplateId, tenantId, request.Question, startedAt);
      record.Result = result;
      record.Status = DebateStatus.Completed;
      record.CompletedAt = DateTimeOffset.UtcNow;
      record.AddRounds(result.Rounds);
      return record;
   }

   public DebateRecord Enqueue(CreateDebateRequest request, string tenantId)
   {
      var builder = ResolveTemplateBuilder(request);
      var record = CreateRecord(request.TemplateId, tenantId, request.Question);

      _ = RunOrchestratorEnqueuedAsync(record, builder);

      return record;
   }

   // ── Scenario path ─────────────────────────────────────────────────────────

   public async Task<DebateRecord> RunScenarioAsync(
      ScenarioRequest scenario,
      string tenantId,
      CancellationToken ct = default)
   {
      var builder = ScenarioBuilder.Build(scenario, _configuration);
      var startedAt = DateTimeOffset.UtcNow;
      var result = await _orchestrator.ExecuteAsync(builder, ct).ConfigureAwait(false);
      var record = CreateRecord("scenario", tenantId, scenario.Label ?? scenario.Question, startedAt);
      record.Result = result;
      record.Status = DebateStatus.Completed;
      record.CompletedAt = DateTimeOffset.UtcNow;
      record.AddRounds(result.Rounds);
      return record;
   }

   public DebateRecord EnqueueScenario(ScenarioRequest scenario, string tenantId)
   {
      if (scenario.Members is not { Length: > 0 })
         throw new ArgumentException("Scenario must have at least one member.");

      var builder = ScenarioBuilder.Build(scenario, _configuration);
      var record = CreateRecord("scenario", tenantId, scenario.Label ?? scenario.Question);

      _ = RunOrchestratorEnqueuedAsync(record, builder);

      return record;
   }

   // ── Common ────────────────────────────────────────────────────────────────

   public DebateRecord? Find(string debateId, string tenantId)
   {
      if (!_records.TryGetValue(debateId, out var r)) return null;

      // Tenant isolation: an unknown id and another tenant's id are indistinguishable
      // on purpose, so a caller cannot probe for the existence of a foreign debate.
      return string.Equals(r.TenantId, tenantId, StringComparison.Ordinal) ? r : null;
   }

   public DebateRecord[] List(string tenantId, string? templateId, string? status, int page, int pageSize)
   {
      // Filter by tenant BEFORE paginating: filtering afterwards would leak other
      // tenants' records through empty pages and shifting offsets.
      var query = _records.Values
         .Where(r => string.Equals(r.TenantId, tenantId, StringComparison.Ordinal));

      if (!string.IsNullOrEmpty(templateId))
         query = query.Where(r =>
            r.TemplateId.Equals(templateId, StringComparison.OrdinalIgnoreCase));

      if (!string.IsNullOrEmpty(status) &&
          Enum.TryParse<DebateStatus>(status, true, out var s))
         query = query.Where(r => r.Status == s);

      return query
         .OrderByDescending(r => r.CreatedAt)
         .Skip((page - 1) * pageSize)
         .Take(pageSize)
         .ToArray();
   }

   public bool Cancel(string debateId, string tenantId)
   {
      if (Find(debateId, tenantId) is not { } record) return false;
      if (record.Status is DebateStatus.Completed or DebateStatus.Failed or DebateStatus.Cancelled) return false;

      record.Status = DebateStatus.Cancelled;
      _logger.LogInformation("Debate {DebateId} cancelled.", debateId);

      _ = Task.Run(async () =>
      {
         try
         {
            await _orchestrator.CancelAsync(debateId).ConfigureAwait(false);
         }
         catch (Exception ex)
         {
            _logger.LogWarning(ex, "Failed to cancel debate {DebateId}.", debateId);
         }
      });
      return true;
   }

   // ── IDisposable ───────────────────────────────────────────────────────────

   /// <summary>
   ///    Disposes the eviction timer.
   /// </summary>
   public void Dispose()
   {
      _evictionTimer.Dispose();
   }

   // ── Eviction ──────────────────────────────────────────────────────────────

   private void EvictCompletedRecords(object? state)
   {
      var cutoff = DateTimeOffset.UtcNow - _completedRecordLifetime;
      foreach (var kvp in _records)
         if (kvp.Value.Status is not DebateStatus.Running && kvp.Value.CompletedAt < cutoff)
            _records.TryRemove(kvp.Key, out _);
   }

   // ── Private ───────────────────────────────────────────────────────────────

   private ICouncilBuilder ResolveTemplateBuilder(CreateDebateRequest request)
   {
      var template = _templates.Get(request.TemplateId) ??
                     throw new InvalidOperationException(
                        $"Template '{request.TemplateId}' is not registered.");
      return template.Configure(request, _services, _configuration);
   }

   /// <param name="createdAt">
   ///    When the debate actually started. Must be stamped <i>before</i> execution begins: on the
   ///    synchronous paths the record is created after the debate returns, so defaulting
   ///    <c>CreatedAt</c> to the object initialiser made it identical to <c>CompletedAt</c> and
   ///    reported a 41-second debate as zero-length.
   /// </param>
   private DebateRecord CreateRecord(string templateId, string tenantId, string label, DateTimeOffset? createdAt = null)
   {
      var record = new DebateRecord
      {
         DebateId = Guid.NewGuid().ToString("N"),
         TemplateId = templateId,
         TenantId = tenantId,
         Label = label,
         CreatedAt = createdAt ?? DateTimeOffset.UtcNow
      };

      _records[record.DebateId] = record;
      return record;
   }

   private async Task RunOrchestratorEnqueuedAsync(DebateRecord record, ICouncilBuilder builder)
   {
      record.Status = DebateStatus.Running;
      _logger.LogInformation("Debate {DebateId} started (enqueued).", record.DebateId);

      try
      {
         await _orchestrator.EnqueueAsync(record.DebateId, builder).ConfigureAwait(false);

         await foreach (var evt in _orchestrator.StreamAsync(record.DebateId).ConfigureAwait(false))
            switch (evt)
            {
               case DebateRoundEvent.RoundCompleted rc:
                  record.Rounds.Enqueue(rc.Round);
                  break;
               case DebateRoundEvent.DebateCompleted dc:
                  record.Result = dc.Result;
                  record.Status = DebateStatus.Completed;
                  record.CompletedAt = dc.Result?.CompletedAt ?? DateTimeOffset.UtcNow;
                  _logger.LogInformation(
                     "Debate {DebateId} completed — {Rounds} rounds.",
                     record.DebateId, record.Rounds.Count);
                  return;
               case DebateRoundEvent.DebateFailed df:
                  record.ErrorMessage = df.Error;
                  record.Status = DebateStatus.Failed;
                  record.CompletedAt = DateTimeOffset.UtcNow;
                  _logger.LogError("Debate {DebateId} failed: {Error}", record.DebateId, df.Error);
                  return;
               case DebateRoundEvent.DebateCancelled:
                  record.Status = DebateStatus.Cancelled;
                  record.CompletedAt = DateTimeOffset.UtcNow;
                  _logger.LogInformation("Debate {DebateId} was cancelled.", record.DebateId);
                  return;
            }
      }
      catch (OperationCanceledException)
      {
         record.Status = DebateStatus.Cancelled;
         _logger.LogInformation("Debate {DebateId} was cancelled.", record.DebateId);
      }
      catch (Exception ex)
      {
         record.Status = DebateStatus.Failed;
         record.ErrorMessage = ex.Message;
         _logger.LogError(ex, "Debate {DebateId} failed.", record.DebateId);
      }
   }
}
