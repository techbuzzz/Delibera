using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

public interface IDebateOrchestrationService
{
    /// <summary>Run debate synchronously (awaits completion).</summary>
    Task<DebateRecord> RunAsync(
        CreateDebateRequest request,
        string tenantId,
        CancellationToken ct = default);

    /// <summary>Enqueue debate for background execution; return immediately.</summary>
    DebateRecord Enqueue(CreateDebateRequest request, string tenantId);

    DebateRecord?  Find(string debateId);
    DebateRecord[] List(string? templateId, string? status, int page, int pageSize);
    bool           Cancel(string debateId);
}
