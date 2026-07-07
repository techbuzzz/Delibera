using System.Collections.Concurrent;
using Delibera.Server.Scenarios;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Services;

/// <summary>
/// In-process orchestrator that runs council debates synchronously or enqueues
/// them for background execution via two paths:
/// <list type="bullet">
///   <item>Template path  — <see cref="CreateDebateRequest"/> resolved via <see cref="ITemplateRegistry"/>.</item>
///   <item>Scenario path  — <see cref="ScenarioRequest"/> built directly via <see cref="ScenarioBuilder"/>.</item>
/// </list>
/// </summary>
public sealed class DebateOrchestrationService : IDebateOrchestrationService
{
    private readonly ILogger<DebateOrchestrationService> _logger;
    private readonly ITemplateRegistry                   _templates;
    private readonly IServiceProvider                    _services;
    private readonly IConfiguration                     _configuration;

    private readonly ConcurrentDictionary<string, DebateRecord> _records = new();

    public DebateOrchestrationService(
        ILogger<DebateOrchestrationService> logger,
        ITemplateRegistry                   templates,
        IServiceProvider                    services,
        IConfiguration                      configuration)
    {
        _logger        = logger;
        _templates     = templates;
        _services      = services;
        _configuration = configuration;
    }

    // ── Template path ─────────────────────────────────────────────────────────

    public async Task<DebateRecord> RunAsync(
        CreateDebateRequest request,
        string              tenantId,
        CancellationToken   ct = default)
    {
        var record = CreateTemplateRecord(request, tenantId);
        await ExecuteTemplateAsync(record, request, ct);
        return record;
    }

    public DebateRecord Enqueue(CreateDebateRequest request, string tenantId)
    {
        var record = CreateTemplateRecord(request, tenantId);
        _ = Task.Run(() => ExecuteTemplateAsync(record, request, CancellationToken.None));
        return record;
    }

    // ── Scenario path ─────────────────────────────────────────────────────────

    public async Task<DebateRecord> RunScenarioAsync(
        ScenarioRequest   scenario,
        string            tenantId,
        CancellationToken ct = default)
    {
        var record = CreateScenarioRecord(scenario, tenantId);
        await ExecuteScenarioAsync(record, scenario, ct);
        return record;
    }

    public DebateRecord EnqueueScenario(ScenarioRequest scenario, string tenantId)
    {
        var record = CreateScenarioRecord(scenario, tenantId);
        _ = Task.Run(() => ExecuteScenarioAsync(record, scenario, CancellationToken.None));
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
        return true;
    }

    // ── Private: template path ────────────────────────────────────────────────

    private DebateRecord CreateTemplateRecord(CreateDebateRequest request, string tenantId)
    {
        var template = _templates.Get(request.TemplateId)
            ?? throw new InvalidOperationException(
                $"Template '{request.TemplateId}' is not registered.");

        _ = template; // validated — will be re-fetched in Execute

        var record = new DebateRecord
        {
            DebateId   = Guid.NewGuid().ToString("N"),
            TemplateId = request.TemplateId,
            TenantId   = tenantId,
            Label      = request.Question,
        };

        _records[record.DebateId] = record;
        _logger.LogInformation(
            "[Template] Debate {DebateId} created for '{TemplateId}' (tenant: {TenantId}).",
            record.DebateId, record.TemplateId, record.TenantId);
        return record;
    }

    private async Task ExecuteTemplateAsync(
        DebateRecord        record,
        CreateDebateRequest request,
        CancellationToken   ct)
    {
        record.Status = DebateStatus.Running;
        _logger.LogInformation("[Template] Debate {DebateId} started.", record.DebateId);

        try
        {
            var template = _templates.Get(record.TemplateId)!;
            var builder  = template.Configure(request, _services, _configuration);
            var executor = builder.Build();

            executor.OnRoundCompleted += round =>
            {
                record.Rounds.Add(round);
                record.RoundWriter.TryWrite(round);
            };

            record.Result      = await executor.ExecuteAsync(ct);
            record.Status      = DebateStatus.Completed;
            record.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "[Template] Debate {DebateId} completed — {Rounds} rounds, {Tokens} tokens.",
                record.DebateId, record.Rounds.Count, record.Result.TokenStats?.GrandTotal);
        }
        catch (OperationCanceledException) when (record.Status == DebateStatus.Cancelled)
        {
            _logger.LogInformation("[Template] Debate {DebateId} was cancelled.", record.DebateId);
        }
        catch (Exception ex)
        {
            record.Status       = DebateStatus.Failed;
            record.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[Template] Debate {DebateId} failed.", record.DebateId);
        }
        finally
        {
            record.RoundWriter.TryComplete();
        }
    }

    // ── Private: scenario path ────────────────────────────────────────────────

    private DebateRecord CreateScenarioRecord(ScenarioRequest scenario, string tenantId)
    {
        if (scenario.Members is not { Length: > 0 })
            throw new ArgumentException("Scenario must have at least one member.");

        var record = new DebateRecord
        {
            DebateId   = Guid.NewGuid().ToString("N"),
            TemplateId = "scenario",
            TenantId   = tenantId,
            Label      = scenario.Label ?? scenario.Question,
        };

        _records[record.DebateId] = record;
        _logger.LogInformation(
            "[Scenario] Debate {DebateId} created — {Members} members (tenant: {TenantId}).",
            record.DebateId, scenario.Members.Length, record.TenantId);
        return record;
    }

    private async Task ExecuteScenarioAsync(
        DebateRecord      record,
        ScenarioRequest   scenario,
        CancellationToken ct)
    {
        record.Status = DebateStatus.Running;
        _logger.LogInformation("[Scenario] Debate {DebateId} started.", record.DebateId);

        try
        {
            var builder  = ScenarioBuilder.Build(scenario, _configuration);
            var executor = builder.Build();

            executor.OnRoundCompleted += round =>
            {
                record.Rounds.Add(round);
                record.RoundWriter.TryWrite(round);
            };

            record.Result      = await executor.ExecuteAsync(ct);
            record.Status      = DebateStatus.Completed;
            record.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "[Scenario] Debate {DebateId} completed — {Rounds} rounds, {Tokens} tokens.",
                record.DebateId, record.Rounds.Count, record.Result.TokenStats?.GrandTotal);
        }
        catch (OperationCanceledException) when (record.Status == DebateStatus.Cancelled)
        {
            _logger.LogInformation("[Scenario] Debate {DebateId} was cancelled.", record.DebateId);
        }
        catch (Exception ex)
        {
            record.Status       = DebateStatus.Failed;
            record.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[Scenario] Debate {DebateId} failed.", record.DebateId);
        }
        finally
        {
            record.RoundWriter.TryComplete();
        }
    }
}
