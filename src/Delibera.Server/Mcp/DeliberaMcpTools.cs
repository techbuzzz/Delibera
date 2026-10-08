using System.ComponentModel;
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
   /// <summary>
   ///    Tenant that owns every debate started through MCP. The MCP surface has no
   ///    <c>X-Tenant-Id</c> header, so it gets its own identity and can only see the
   ///    debates it started itself — the same isolation the HTTP surface enforces.
   /// </summary>
   private const string McpTenantId = "mcp";
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
         KnowledgeText = context
      };

      var record = await orchestration.RunAsync(request, McpTenantId, ct);
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
         members = JsonSerializer.Deserialize<ScenarioMember[]>(membersJson) ??
                   throw new InvalidOperationException("membersJson deserialised to null.");
      }
      catch (Exception ex)
      {
         // Serialise the message instead of interpolating it: an exception message may
         // contain quotes or backslashes, which would produce invalid JSON for the model.
         return ErrorPayload($"Invalid membersJson: {ex.Message}");
      }

      var scenario = new ScenarioRequest
      {
         Question = question,
         Members = members,
         // The tool documents "Standard|Critique|Consensus" and the HTTP path falls back
         // to Standard for anything unknown; a null must do the same rather than flowing
         // into a non-nullable member.
         Strategy = strategy ?? "Standard",
         VotingStrategy = votingStrategy,
         // The tool advertises "1-10" but never enforced it, while list_debates already
         // clamps pageSize. An anonymous MCP client could otherwise ask for any number of
         // paid LLM rounds.
         MaxRounds = Math.Clamp(maxRounds ?? 3, 1, 10),
         Chairman = chairmanPrompt is null
            ? null
            : new ScenarioChairman { SystemPrompt = chairmanPrompt },
         KnowledgeText = knowledgeText
      };

      var record = await orchestration.RunScenarioAsync(scenario, McpTenantId, ct);
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
      var record = orchestration.Find(debateId, McpTenantId);
      return record is null
         ? ErrorPayload($"Debate '{debateId}' not found.")
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
      var records = orchestration.List(McpTenantId, templateId, status, page, pageSize);

      var summaries = records.Select(r => new
      {
         r.DebateId,
         r.TemplateId,
         r.TenantId,
         Status = r.Status.ToString(),
         r.CreatedAt,
         r.CompletedAt,
         Verdict = r.Result?.FinalVerdict
      });

      return JsonSerializer.Serialize(summaries,
         new JsonSerializerOptions
         {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
         });
   }

   // ── 5. cancel_debate ──────────────────────────────────────────────────────

   [McpServerTool]
   [Description("Cancel a running or pending debate. Returns true if cancelled, false if already finished.")]
   public string cancel_debate(
      [Description("The debate ID to cancel.")]
      string debateId)
   {
      var cancelled = orchestration.Cancel(debateId, McpTenantId);
      return JsonSerializer.Serialize(new { debateId, cancelled });
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
         t.Description
      });

      return JsonSerializer.Serialize(list,
         new JsonSerializerOptions { WriteIndented = true });
   }

   // ── Helpers ───────────────────────────────────────────────────────────────

   /// <summary>
   ///    Builds an <c>{ "error": … }</c> payload. Real serialisation, so a message
   ///    containing quotes, backslashes or newlines cannot produce invalid JSON.
   /// </summary>
   private static string ErrorPayload(string message)
   {
      return JsonSerializer.Serialize(
         new { error = message },
         new JsonSerializerOptions { WriteIndented = true });
   }

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
         Rounds = record.RoundsSnapshot().Select(r => new
         {
            r.RoundNumber,
            // Speeches → Responses (словарь)
            Speeches = r.Responses.Select(s => new
            {
               MemberRole = s.Key,
               Content = s.Value
            })
         }),
         record.CreatedAt,
         record.CompletedAt,
         record.ErrorMessage
      };

      return JsonSerializer.Serialize(payload,
         new JsonSerializerOptions
         {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
         });
   }
}
