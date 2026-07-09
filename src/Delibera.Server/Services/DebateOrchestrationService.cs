using System.Collections.Concurrent;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Server.Scenarios;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Services;

/// <summary>
///    HTTP-oriented orchestrator that bridges <see cref="CreateDebateRequest" /> /
///    <see cref="ScenarioRequest" /> to <see cref="ICouncilBuilder" /> and delegates
///    execution to <see cref="IDebateOrchestrator" />.
///    <para>
///       When <see cref="IDebateOrchestrator" /> is a <see cref="LocalDebateOrchestrator" />,
///       debates run in-process (the default). When it's a <c>RedisDebateOrchestrator</c>,
///       round events are published to Redis Streams for cross-instance SSE streaming.
///    </para>
/// </summary>
public sealed class DebateOrchestrationService : IDebateOrchestrationService
{
    private readonly ILogger<DebateOrchestrationService> _logger;
    private readonly ITemplateRegistry _templates;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly IDebateOrchestrator _orchestrator;

    private readonly ConcurrentDictionary<string, DebateRecord> _records = new();

    public DebateOrchestrationService(
        ILogger<DebateOrchestrationService> logger,
        ITemplateRegistry templates,
        IServiceProvider services,
        IConfiguration configuration,
        IDebateOrchestrator orchestrator)
    {
        _logger = logger;
        _templates = templates;
        _services = services;
        _configuration = configuration;
        _orchestrator = orchestrator;
    }

    // ── Template path ─────────────────────────────────────────────────────────

    public async Task<DebateRecord> RunAsync(
        CreateDebateRequest request,
        string tenantId,
        CancellationToken ct = default)
    {
        var builder = ResolveTemplateBuilder(request);
        var record = CreateRecord(request.TemplateId, tenantId, request.Question);

        var result = await _orchestrator.ExecuteAsync(builder, ct);
        record.Result = result;
        record.Status = DebateStatus.Completed;
        record.CompletedAt = DateTimeOffset.UtcNow;
        record.Rounds.AddRange(result.Rounds);

        _logger.LogInformation(
            "[Template] Debate {DebateId} completed — {Rounds} rounds, {Tokens} tokens.",
            record.DebateId, record.Rounds.Count, result.TokenStats?.GrandTotal);

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
        var record = CreateRecord("scenario", tenantId, scenario.Label ?? scenario.Question);

        var result = await _orchestrator.ExecuteAsync(builder, ct);
        record.Result = result;
        record.Status = DebateStatus.Completed;
        record.CompletedAt = DateTimeOffset.UtcNow;
        record.Rounds.AddRange(result.Rounds);

        _logger.LogInformation(
            "[Scenario] Debate {DebateId} completed — {Rounds} rounds, {Tokens} tokens.",
            record.DebateId, record.Rounds.Count, result.TokenStats?.GrandTotal);

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

    public DebateRecord? Find(string debateId)
        => _records.TryGetValue(debateId, out var r) ? r : null;

    public DebateRecord[] List(string? templateId, string? status, int page, int pageSize)
    {
        IEnumerable<DebateRecord> query = _records.Values;

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

    public bool Cancel(string debateId)
    {
        if (!_records.TryGetValue(debateId, out var record)) return false;
        if (record.Status is DebateStatus.Completed or DebateStatus.Failed) return false;

        record.Status = DebateStatus.Cancelled;
        record.RoundWriter.TryComplete();
        _logger.LogInformation("Debate {DebateId} cancelled.", debateId);

        _ = _orchestrator.CancelAsync(debateId);
        return true;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private ICouncilBuilder ResolveTemplateBuilder(CreateDebateRequest request)
    {
        var template = _templates.Get(request.TemplateId)
            ?? throw new InvalidOperationException(
                $"Template '{request.TemplateId}' is not registered.");
        return template.Configure(request, _services, _configuration);
    }

    private DebateRecord CreateRecord(string templateId, string tenantId, string label)
    {
        var record = new DebateRecord
        {
            DebateId = Guid.NewGuid().ToString("N"),
            TemplateId = templateId,
            TenantId = tenantId,
            Label = label,
        };

        _records[record.DebateId] = record;
        return record;
    }

    private async Task RunOrchestratorEnqueuedAsync(DebateRecord record, ICouncilBuilder builder)
    {
        record.Status = DebateStatus.Running;
        _logger.LogInformation("Debate {DebateId} started (enqueued).", record.DebateId);

        // Subscribe to round events from the orchestrator
        var streamTask = Task.Run(async () =>
        {
            await foreach (var evt in _orchestrator.StreamAsync(record.DebateId))
            {
                switch (evt)
                {
                    case DebateRoundEvent.RoundCompleted rc:
                        record.Rounds.Add(rc.Round);
                        record.RoundWriter.TryWrite(rc.Round);
                        break;
                    case DebateRoundEvent.DebateCompleted:
                    case DebateRoundEvent.DebateFailed:
                    case DebateRoundEvent.DebateCancelled:
                        break;
                }
            }
        });

        try
        {
            var handle = await _orchestrator.EnqueueAsync(record.DebateId, builder);

            // Poll until completion
            while (true)
            {
                await Task.Delay(200);
                var status = await _orchestrator.GetStatusAsync(record.DebateId);
                if (status?.Status is DebateOrchestrationStatus.Completed
                                 or DebateOrchestrationStatus.Failed
                                 or DebateOrchestrationStatus.Cancelled)
                {
                    record.Result = status.Result;
                    record.ErrorMessage = status.ErrorMessage;
                    record.CompletedAt = status.CompletedAt;
                    record.Status = status.Status switch
                    {
                        DebateOrchestrationStatus.Completed => DebateStatus.Completed,
                        DebateOrchestrationStatus.Failed => DebateStatus.Failed,
                        DebateOrchestrationStatus.Cancelled => DebateStatus.Cancelled,
                        _ => DebateStatus.Failed
                    };
                    break;
                }
            }

            _logger.LogInformation(
                "Debate {DebateId} {Status} — {Rounds} rounds.",
                record.DebateId, record.Status, record.Rounds.Count);
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
        finally
        {
            record.RoundWriter.TryComplete();
            await streamTask; // ensure streaming completes
        }
    }
}