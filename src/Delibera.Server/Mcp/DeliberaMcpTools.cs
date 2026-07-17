using System.ComponentModel;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;
using Delibera.Server.Templates.Registry;
using ModelContextProtocol.Server;

namespace Delibera.Server.Mcp;

/// <summary>
///    Exposes the Delibera council as six MCP tools consumable by any
///    MCP-compliant client (Claude Desktop, Cursor, custom agents, …).
///    Registered via <c>WithToolsFromAssembly()</c> in
///    <see cref="McpEndpointExtensions" />.
/// </summary>
[McpServerToolType]
public sealed class DeliberaMcpTools(
   IDebateOrchestrationService orchestration,
   ITemplateRegistry templates)
{
   // ── 1. run_debate_template ────────────────────────────────────────────────

   [McpServerTool]
   [Description("""
                Run a Delibera council debate using a built-in template and wait for
                the verdict. Available templateIds: risk-committee, architecture-decision,
                code-review, requirements-review, legal-contract-review.
                Returns the debate record as JSON including verdict and rounds.
                """)]
   public async Task<string> run_debate_template(
      [Description("Template ID, e.g. 'code-review' or 'architecture-decision'.")]
      string templateId,
      [Description("The question or artefact the council should deliberate on.")]
      string question,
      [Description("Optional free-text context injected as knowledge (RAG-free).")]
      string? context,
      CancellationToken ct = default)
   {
      var request = new CreateDebateRequest
      {
         TemplateId = templateId,
         Question = question,
         KnowledgeText = context,
      };

      var record = await orchestration.RunAsync(request, "mcp", ct);
      return SerialiseRecord(record);
   }

   // ── 2. run_debate_scenario ────────────────────────────────────────────────

   [McpServerTool]
   [Description("""
                Run a fully customisable ad-hoc debate scenario and wait for the verdict.
                Supply members as a JSON array: [{"role":"Architect","persona":"…"},…].
                Optional fields: strategy (Standard|Critique|Consensus),
                votingStrategy (Majority|BordaCount|Weighted), maxRounds (1-10),
                chairmanPrompt, knowledgeText.
                Returns the debate record as JSON.
                """)]
   public async Task<string> run_debate_scenario(
      [Description("The question the council should deliberate on.")]
      string question,
      [Description("Council members as JSON array: [{\"role\":\"…\",\"persona\":\"…\"}].")]
      string membersJson,
      [Description("Debate strategy: Standard (default), Critique, or Consensus.")]
      string? strategy,
      [Description("Voting strategy: Majority (default), BordaCount, or Weighted.")]
      string? votingStrategy,
      [Description("Maximum debate rounds (1-10, default 3).")]
      int? maxRounds,
      [Description("Custom chairman system prompt for verdict synthesis.")]
      string? chairmanPrompt,
      [Description("Optional free-text knowledge injected into the debate context.")]
      string? knowledgeText,
      CancellationToken ct = default)
   {
      ScenarioMember[] members;
      try
      {
         members = System.Text.Json.JsonSerializer.Deserialize<ScenarioMember[]>(membersJson) ?? throw new InvalidOperationException("membersJson deserialised to null.");
      }
      catch (Exception ex)
      {
         return $"{{\"error\":\"Invalid membersJson: {ex.Message}\"}}";
      }

      var scenario = new ScenarioRequest
      {
         Question = question,
         Members = members,
         Strategy = strategy,
         VotingStrategy = votingStrategy,
         MaxRounds = maxRounds ?? 3,
         Chairman = chairmanPrompt is null
            ? null
            : new ScenarioChairman { SystemPrompt = chairmanPrompt },
         KnowledgeText = knowledgeText,
      };

      var record = await orchestration.RunScenarioAsync(scenario, "mcp", ct);
      return SerialiseRecord(record);
   }

   // ── 3. get_debate ─────────────────────────────────────────────────────────

   [McpServerTool]
   [Description("""
                Retrieve a debate record by its ID.
                Returns the full record (status, verdict, rounds) as JSON,
                or an error object if not found.
                """)]
   public string get_debate(
      [Description("The debate ID returned by run_debate_template or run_debate_scenario.")]
      string debateId)
   {
      var record = orchestration.Find(debateId);
      return record is null
         ? $"{{\"error\":\"Debate '{debateId}' not found.\"}}"
         : SerialiseRecord(record);
   }

   // ── 4. list_debates ───────────────────────────────────────────────────────

   [McpServerTool]
   [Description("""
                List debate records with optional filtering.
                Returns a JSON array of summary objects.
                """)]
   public string list_debates(
      [Description("Filter by templateId (e.g. 'code-review'). Null returns all templates.")]
      string? templateId,
      [Description("Filter by status: Pending, Running, Completed, Failed, Cancelled.")]
      string? status,
      [Description("1-based page number (default 1).")]
      int page = 1,
      [Description("Page size (default 20, max 100).")]
      int pageSize = 20)
   {
      pageSize = Math.Clamp(pageSize, 1, 100);
      var records = orchestration.List(templateId, status, page, pageSize);

      var summaries = records.Select(r => new
      {
         r.DebateId,
         r.TemplateId,
         r.TenantId,
         Status = r.Status.ToString(),
         r.CreatedAt,
         r.CompletedAt,
         Verdict = r.Result?.FinalVerdict,
      });

      return System.Text.Json.JsonSerializer.Serialize(summaries,
         new System.Text.Json.JsonSerializerOptions
         {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
         });
   }

   // ── 5. cancel_debate ──────────────────────────────────────────────────────

   [McpServerTool]
   [Description("Cancel a running or pending debate. Returns true if cancelled, false if already finished.")]
   public string cancel_debate(
      [Description("The debate ID to cancel.")]
      string debateId)
   {
      var cancelled = orchestration.Cancel(debateId);
      return System.Text.Json.JsonSerializer.Serialize(new { debateId, cancelled });
   }

   // ── 6. list_templates ─────────────────────────────────────────────────────

   [McpServerTool]
   [Description("""
                List all registered Delibera debate templates.
                Use the returned templateId values with run_debate_template.
                """)]
   public string list_templates()
   {
      var list = templates.GetAll().Select(t => new
      {
         t.TemplateId,
         t.DisplayName,
         t.Description,
      });

      return System.Text.Json.JsonSerializer.Serialize(list,
         new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
   }

   // ── Helpers ───────────────────────────────────────────────────────────────

   private static string SerialiseRecord(DebateRecord record)
   {
      var result = record.Result;
      var payload = new
      {
         record.DebateId,
         record.TemplateId,
         record.TenantId,
         Status = record.Status.ToString(),
         // Verdict → FinalVerdict
         Verdict = result?.FinalVerdict,
         // Confidence — нет в DebateResult; парсим из FinalVerdict или убираем
         Confidence = (object?)null,
         Rationale = (object?)null,
         Risks = (object?)null,
         Rounds = record.Rounds.Select(r => new
         {
            r.RoundNumber,
            // Speeches → Responses (словарь)
            Speeches = r.Responses.Select(s => new
            {
               MemberRole = s.Key,
               Content = s.Value,
            }),
         }),
         record.CreatedAt,
         record.CompletedAt,
         record.ErrorMessage,
      };

      return System.Text.Json.JsonSerializer.Serialize(payload,
         new System.Text.Json.JsonSerializerOptions
         {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
         });
   }
}
