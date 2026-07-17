using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

public interface IDebateOrchestrationService
{
   // ── Template-based ────────────────────────────────────────────────────────

   /// <summary>Run a template-based debate synchronously (awaits completion).</summary>
   Task<DebateRecord> RunAsync(
      CreateDebateRequest request,
      string tenantId,
      CancellationToken ct = default);

   /// <summary>Enqueue a template-based debate for background execution.</summary>
   DebateRecord Enqueue(CreateDebateRequest request, string tenantId);

   // ── Scenario-based ────────────────────────────────────────────────────────

   /// <summary>Run an ad-hoc scenario debate synchronously (awaits completion).</summary>
   Task<DebateRecord> RunScenarioAsync(
      ScenarioRequest scenario,
      string tenantId,
      CancellationToken ct = default);

   /// <summary>Enqueue an ad-hoc scenario debate for background execution.</summary>
   DebateRecord EnqueueScenario(ScenarioRequest scenario, string tenantId);

   // ── Common ────────────────────────────────────────────────────────────────

   DebateRecord? Find(string debateId);
   DebateRecord[] List(string? templateId, string? status, int page, int pageSize);
   bool Cancel(string debateId);
}
