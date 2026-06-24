using System.Text.Json;

namespace Delibera.Core.Council;

/// <summary>
///    The Operator — a special council role implemented as a self-contained micro-agent.
///    It connects to one or more MCP (Model Context Protocol) servers, exposes their tools
///    to debate participants, and fulfils natural-language tasks delegated by participants:
///    it interprets the task with its own (typically cheaper) LLM model, selects and invokes
///    the appropriate MCP tools, interprets the raw results, optionally compresses them using
///    the debate's compression strategy, and returns a concise answer to the requester.
/// </summary>
/// <remarks>
///    The Operator deliberately uses a less expensive model than the debate participants —
///    its job is orchestration and interpretation of tool output, not deep reasoning, so a
///    cheaper model keeps tool usage economical.
/// </remarks>
public sealed class Operator : IOperator
{
   private readonly CompressionOptions? _compressionOptions;
   private readonly IContextCompressor? _compressor;
   private readonly List<OperatorInteraction> _interactions = [];
   private readonly Dictionary<string, IMcpClient> _mcpClients;
   private readonly CouncilMember _model;
   private readonly List<OperatorTool> _tools = [];

   /// <summary>
   ///    Creates an Operator.
   /// </summary>
   /// <param name="model">The (cheaper) LLM model the Operator uses to plan and interpret tool output.</param>
   /// <param name="mcpClients">MCP server clients the Operator can use.</param>
   /// <param name="compressor">Optional compressor used to shrink large tool results before returning them.</param>
   /// <param name="compressionOptions">Optional compression options.</param>
   public Operator(
      CouncilMember model,
      IEnumerable<IMcpClient> mcpClients,
      IContextCompressor? compressor = null,
      CompressionOptions? compressionOptions = null)
   {
      _model = model ?? throw new ArgumentNullException(nameof(model));
      ArgumentNullException.ThrowIfNull(mcpClients);

      _mcpClients = mcpClients.ToDictionary(c => c.ServerName, StringComparer.OrdinalIgnoreCase);
      _compressor = compressor;
      _compressionOptions = compressionOptions;

      if (_model.Role is null or "Expert")
         _model.Role = "Operator";
   }

   /// <inheritdoc />
   public string DisplayName => $"🛠️ Operator ({_model.ModelName})";

   /// <inheritdoc />
   public bool IsInitialized { get; private set; }

   /// <inheritdoc />
   public IReadOnlyList<OperatorTool> AvailableTools => _tools.AsReadOnly();

   /// <inheritdoc />
   public IReadOnlyList<OperatorInteraction> Interactions => _interactions.AsReadOnly();

   /// <inheritdoc />
   public async Task InitializeAsync(CancellationToken ct = default)
   {
      if (IsInitialized) return;

      _tools.Clear();
      foreach (var client in _mcpClients.Values)
         try
         {
            await client.ConnectAsync(ct);
            var tools = await client.ListToolsAsync(ct);
            _tools.AddRange(tools);
         }
         catch (Exception ex)
         {
            // A failing server should not abort the whole debate — skip it but keep going.
            _interactions.Add(new OperatorInteraction(
               "System",
               $"Connect to MCP server '{client.ServerName}'",
               $"[Failed to initialise server '{client.ServerName}': {ex.Message}]",
               [],
               false,
               DateTime.UtcNow));
         }

      IsInitialized = true;
   }

   /// <inheritdoc />
   public string GetToolCatalog()
   {
      if (_tools.Count == 0)
         return "The Operator currently has no tools available.";

      var sb = new StringBuilder();
      sb.AppendLine("The Operator can perform the following actions via connected MCP servers:");
      foreach (var byServer in _tools.GroupBy(t => t.ServerName))
      {
         sb.AppendLine($"\n• Server \"{byServer.Key}\":");
         foreach (var tool in byServer)
         {
            var desc = string.IsNullOrWhiteSpace(tool.Description)
               ? "(no description)"
               : tool.Description.Trim();
            sb.AppendLine($"   - {tool.QualifiedName}: {desc}");
         }
      }

      return sb.ToString();
   }

   /// <inheritdoc />
   public async Task<OperatorResult> ExecuteTaskAsync(
      string requesterName,
      string task,
      CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(requesterName);
      ArgumentException.ThrowIfNullOrWhiteSpace(task);

      if (!IsInitialized)
         await InitializeAsync(ct);

      // 1. Plan: ask the model which tool(s) to call.
      var plannedCalls = _tools.Count == 0
         ? []
         : await PlanToolCallsAsync(task, ct);

      // 2. Execute the planned tool calls.
      var executed = new List<OperatorToolCall>();
      foreach (var plan in plannedCalls)
      {
         if (!_mcpClients.TryGetValue(plan.ServerName, out var client))
         {
            executed.Add(new OperatorToolCall(plan.ServerName, plan.ToolName, plan.Arguments,
               $"[Unknown MCP server '{plan.ServerName}']", true));
            continue;
         }

         var toolResult = await client.CallToolAsync(plan.ToolName, plan.Arguments, ct);
         executed.Add(new OperatorToolCall(plan.ServerName, plan.ToolName, plan.Arguments,
            toolResult.Text, toolResult.IsError));
      }

      // 3. Interpret the results (or answer directly if no tools were used).
      var answer = await InterpretAsync(requesterName, task, executed, ct);

      // 4. Optionally compress the answer using the debate's compression strategy.
      var compressed = false;
      if (_compressor is not null && !string.IsNullOrWhiteSpace(answer))
         try
         {
            var result = await _compressor.CompressAsync(answer, _compressionOptions, ct);
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
               answer = result.Text;
               compressed = true;
            }
         }
         catch
         {
            // Compression is best-effort; keep the uncompressed answer on failure.
         }

      var operatorResult = new OperatorResult(requesterName, task, answer, executed.AsReadOnly(), compressed);
      _interactions.Add(operatorResult.ToInteraction());
      return operatorResult;
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      foreach (var client in _mcpClients.Values)
         try
         {
            await client.DisposeAsync();
         }
         catch
         {
            // ignore disposal errors
         }
   }

   // ──────────────────────────────────────────────
   // Micro-agent internals
   // ──────────────────────────────────────────────

   private async Task<IReadOnlyList<OperatorToolCall>> PlanToolCallsAsync(string task, CancellationToken ct)
   {
      const string systemPrompt = """
                                  You are the Operator's planner — a tool-routing micro-agent.
                                  Given a task and a list of available MCP tools, decide which tools to call.
                                  Respond with STRICT JSON only, no prose, in exactly this shape:
                                  {"tool_calls":[{"server":"<server>","tool":"<tool>","arguments":{ ... }}]}
                                  Rules:
                                  - Use only tools from the provided list (match server and tool names exactly).
                                  - Provide arguments that satisfy each tool's input schema.
                                  - If no tool is appropriate, return {"tool_calls":[]}.
                                  - Do not wrap the JSON in markdown fences.
                                  """;

      var toolsText = new StringBuilder();
      foreach (var tool in _tools)
         toolsText.AppendLine($"- server=\"{tool.ServerName}\" tool=\"{tool.Name}\" description=\"{tool.Description}\" input_schema={tool.InputSchemaJson}");

      var userPrompt = $"""
                        ### Available tools:
                        {toolsText}

                        ### Task:
                        {task}

                        Return the JSON plan now.
                        """;

      string raw;
      try
      {
         raw = await _model.AskAsync(systemPrompt, userPrompt, 0.1f, ct);
      }
      catch
      {
         return [];
      }

      return ParsePlan(raw);
   }

   private static IReadOnlyList<OperatorToolCall> ParsePlan(string raw)
   {
      var json = ExtractJson(raw);
      if (string.IsNullOrWhiteSpace(json)) return [];

      try
      {
         using var doc = JsonDocument.Parse(json);
         if (!doc.RootElement.TryGetProperty("tool_calls", out var callsEl) ||
             callsEl.ValueKind != JsonValueKind.Array)
            return [];

         var calls = new List<OperatorToolCall>();
         foreach (var callEl in callsEl.EnumerateArray())
         {
            var server = callEl.TryGetProperty("server", out var s)
               ? s.GetString()
               : null;
            var tool = callEl.TryGetProperty("tool", out var t)
               ? t.GetString()
               : null;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool))
               continue;

            var args = new Dictionary<string, object?>();
            if (callEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
               foreach (var prop in argsEl.EnumerateObject())
                  args[prop.Name] = JsonElementToObject(prop.Value);

            // ResultText is filled in after execution; placeholder here.
            calls.Add(new OperatorToolCall(server, tool, args, string.Empty, false));
         }

         return calls;
      }
      catch
      {
         return [];
      }
   }

   private async Task<string> InterpretAsync(
      string requesterName,
      string task,
      IReadOnlyList<OperatorToolCall> toolCalls,
      CancellationToken ct)
   {
      string systemPrompt;
      string userPrompt;

      if (toolCalls.Count == 0)
      {
         systemPrompt = """
                        You are the Operator — an assistant micro-agent serving an AI council debate.
                        No MCP tool was applicable to this task. Answer the requester as best you can
                        from general knowledge, and clearly state that the answer is not tool-sourced.
                        Be concise and factual.
                        """;
         userPrompt = $"""
                       Requester: {requesterName}
                       Task: {task}

                       Provide a concise answer.
                       """;
      }
      else
      {
         var resultsText = new StringBuilder();
         for (var i = 0; i < toolCalls.Count; i++)
         {
            var c = toolCalls[i];
            resultsText.AppendLine($"[Tool {i + 1}] {c.ServerName}.{c.ToolName}{(c.IsError ? " (ERROR)" : "")}");
            resultsText.AppendLine(c.ResultText);
            resultsText.AppendLine();
         }

         systemPrompt = """
                        You are the Operator — an assistant micro-agent serving an AI council debate.
                        You delegated the requester's task to MCP tools and received their raw output below.
                        Interpret and synthesise the results into a clear, concise answer for the requester.
                        Cite which tool(s) produced the information. Do not invent facts beyond the tool output.
                        If a tool errored or returned nothing useful, say so plainly.
                        """;
         userPrompt = $"""
                       Requester: {requesterName}
                       Task: {task}

                       ### Raw tool output:
                       {resultsText}

                       Synthesise the final answer for the requester now.
                       """;
      }

      try
      {
         return await _model.AskAsync(systemPrompt, userPrompt, 0.3f, ct);
      }
      catch (Exception ex)
      {
         return $"[Operator failed to interpret results: {ex.Message}]";
      }
   }

   // ──────────────────────────────────────────────
   // JSON helpers
   // ──────────────────────────────────────────────

   /// <summary>Extracts the first JSON object from a model response, tolerating markdown fences/prose.</summary>
   private static string ExtractJson(string raw)
   {
      if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

      var start = raw.IndexOf('{');
      var end = raw.LastIndexOf('}');
      return start >= 0 && end > start
         ? raw[start..(end + 1)]
         : string.Empty;
   }

   private static object? JsonElementToObject(JsonElement el)
   {
      return el.ValueKind switch
      {
         JsonValueKind.String => el.GetString(),
         JsonValueKind.Number => el.TryGetInt64(out var l)
            ? l
            : el.GetDouble(),
         JsonValueKind.True => true,
         JsonValueKind.False => false,
         JsonValueKind.Null => null,
         JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
         JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
         _ => el.GetRawText()
      };
   }
}