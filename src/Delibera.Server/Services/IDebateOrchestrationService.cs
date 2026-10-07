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
   //
   // Every lookup is tenant-scoped by signature. DebateRecord already carried a
   // TenantId, but nothing compared it with the caller, so any request could read,
   // cancel or export a debate belonging to another tenant. The tenant is a required
   // parameter rather than an optional filter so that no call site can forget it.

   /// <summary>Find a debate owned by <paramref name="tenantId" />, or null.</summary>
   DebateRecord? Find(string debateId, string tenantId);

   /// <summary>List the debates owned by <paramref name="tenantId" />, newest first.</summary>
   DebateRecord[] List(string tenantId, string? templateId, string? status, int page, int pageSize);

   /// <summary>Cancel a debate owned by <paramref name="tenantId" />.</summary>
   bool Cancel(string debateId, string tenantId);
}
