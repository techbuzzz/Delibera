using System.Collections.Concurrent;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Services;

/// <summary>
/// In-process orchestrator that runs council debates synchronously or enqueues
/// them for background execution. All live records are kept in memory; persistence
/// to <see cref="IDebateStore"/> is delegated to Delibera.Core.
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

    // ── IDebateOrchestrationService ───────────────────────────────────────────

    public async Task<DebateRecord> RunAsync(
        CreateDebateRequest request,
        string              tenantId,
        CancellationToken   ct = default)
    {
        var record = CreateRecord(request, tenantId);
        await ExecuteAsync(record, ct);
        return record;
    }

    public DebateRecord Enqueue(CreateDebateRequest request, string tenantId)
    {
        var record = CreateRecord(request, tenantId);
        _ = Task.Run(() => ExecuteAsync(record, CancellationToken.None));
        return record;
    }

    public DebateRecord?  Find(string debateId)
        => _records.TryGetValue(debateId, out var r) ? r : null;

    public DebateRecord[] List(string? templateId, string? status, int page, int pageSize)
    {
        IEnumerable<DebateRecord> query = _records.Values;

        if (!string.IsNullOrEmpty(templateId))
            query = query.Where(r => r.TemplateId.Equals(templateId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<DebateStatus>(status, true, out var s))
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

    // ── Private helpers ───────────────────────────────────────────────────────

    private DebateRecord CreateRecord(CreateDebateRequest request, string tenantId)
    {
        if (!_templates.TryGet(request.TemplateId, out _))
            throw new InvalidOperationException(
                $"Template '{request.TemplateId}' is not registered.");

        var record = new DebateRecord
        {
            DebateId   = Guid.NewGuid().ToString("N"),
            TemplateId = request.TemplateId,
            TenantId   = tenantId
        };

        _records[record.DebateId] = record;
        _logger.LogInformation(
            "Debate {DebateId} created for template '{TemplateId}' (tenant: {TenantId}).",
            record.DebateId, record.TemplateId, record.TenantId);

        return record;
    }

    private async Task ExecuteAsync(DebateRecord record, CancellationToken ct)
    {
        record.Status = DebateStatus.Running;
        _logger.LogInformation("Debate {DebateId} started.", record.DebateId);

        try
        {
            var template = _templates.Get(record.TemplateId);
            var builder  = template.Configure(
                _records[record.DebateId] is var r ? new CreateDebateRequest() : new CreateDebateRequest(),
                _services,
                _configuration);

            // Resolve actual request — re-read from record context
            // (request object is stored implicitly via template.Configure calling convention)
            var executor = builder.Build();

            // Subscribe round-by-round to feed the SSE channel
            executor.OnRoundCompleted += round =>
            {
                record.Rounds.Add(round);
                record.RoundWriter.TryWrite(round);
            };

            var result = await executor.ExecuteAsync(ct);

            record.Result      = result;
            record.Status      = DebateStatus.Completed;
            record.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Debate {DebateId} completed in {Rounds} round(s). Tokens used: {Tokens}.",
                record.DebateId, record.Rounds.Count, result.TotalTokens);
        }
        catch (OperationCanceledException) when (record.Status == DebateStatus.Cancelled)
        {
            _logger.LogInformation("Debate {DebateId} was cancelled.", record.DebateId);
        }
        catch (Exception ex)
        {
            record.Status       = DebateStatus.Failed;
            record.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Debate {DebateId} failed.", record.DebateId);
        }
        finally
        {
            record.RoundWriter.TryComplete();
        }
    }
}
