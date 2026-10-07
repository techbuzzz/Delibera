using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Tools;

/// <summary>
///    Runs one member's turn, invoking tools when the model asks for them.
/// </summary>
/// <remarks>
///    <para>
///    <b>Two transports, one log type.</b> The obvious implementation — wrap any
///    <see cref="ILLMProvider" /> with <c>AsChatClient()</c> and add
///    <c>UseFunctionInvocation()</c> — does not work for most providers. The adapter returned by
///    <c>AsChatClient</c> calls <c>ChatAsync</c>, ignores <c>ChatOptions.Tools</c>, and rebuilds
///    the reply as a single assistant text message; it also flattens every inbound message
///    through <c>message.Text</c>, and function-call content has no text. The middleware
///    therefore never sees a tool request and never delivers a tool result: function invocation
///    on top of that adapter is inert.
///    </para>
///    <para>
///    So a provider that genuinely wraps an <see cref="IChatClient" />
///    (<see cref="ChatClientLLMProvider" />) uses native function calling, and everything else
///    uses the marker protocol that already exists for the Operator. Both paths populate the same
///    <see cref="ToolCallLog" />, so a consumer does not care which one ran.
///    </para>
/// </remarks>
public static class ToolCallingMemberExecutor
{
   /// <summary>
   ///    Produces one member response, running the tool loop as needed.
   /// </summary>
   /// <param name="member">The member taking its turn.</param>
   /// <param name="tools">Tools the member may call; empty means a plain single call.</param>
   /// <param name="systemPrompt">Base system prompt.</param>
   /// <param name="userPrompt">User prompt for this round.</param>
   /// <param name="temperature">Sampling temperature.</param>
   /// <param name="maxIterations">Maximum tool round-trips before the loop gives up.</param>
   /// <param name="roundNumber">Round being executed, recorded on each log entry.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The final response text plus every tool call made along the way.</returns>
   public static async Task<(string Response, IReadOnlyList<ToolCallLog> Calls)> AskAsync(
      CouncilMember member,
      IReadOnlyList<AIFunction> tools,
      string systemPrompt,
      string userPrompt,
      float temperature,
      int maxIterations,
      int roundNumber,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(member);

      var calls = new List<ToolCallLog>();
      if (tools.Count == 0)
         return (await member.AskAsync(systemPrompt, userPrompt, temperature, ct).ConfigureAwait(false), calls);

      return member.Provider is ChatClientLLMProvider chatProvider
         ? await AskNativeAsync(
               chatProvider, member, tools, systemPrompt, userPrompt, temperature, maxIterations, roundNumber, calls, ct)
            .ConfigureAwait(false)
         : await AskWithMarkersAsync(
               member, tools, systemPrompt, userPrompt, temperature, maxIterations, roundNumber, calls, ct)
            .ConfigureAwait(false);
   }

   /// <summary>
   ///    Native function calling: <c>FunctionInvokingChatClient</c> drives the loop, so tool
   ///    requests and results travel as real message content rather than as text.
   /// </summary>
   private static async Task<(string Response, IReadOnlyList<ToolCallLog> Calls)> AskNativeAsync(
      ChatClientLLMProvider provider,
      CouncilMember member,
      IReadOnlyList<AIFunction> tools,
      string systemPrompt,
      string userPrompt,
      float temperature,
      int maxIterations,
      int roundNumber,
      List<ToolCallLog> calls,
      CancellationToken ct)
   {
      var client = provider.ChatClient
         .AsBuilder()
         .UseFunctionInvocation()
         .Build();

      var messages = new List<ChatMessage>
      {
         new(ChatRole.System, systemPrompt + ToolCallParser.BuildBriefing(tools)),
         new(ChatRole.User, userPrompt)
      };

      // Bounded so a model that keeps requesting tools cannot spin. The middleware has its own
      // iteration cap, but this loop re-asks after each completed exchange, so it needs one too.
      var iterations = Math.Max(1, maxIterations);

      // ChatOptions.Tools is IList<AITool>, and IList is invariant, so the AIFunction list has
      // to be copied element-by-element rather than cast.
      var aiTools = new List<AITool>(tools.Count);
      foreach (var tool in tools) aiTools.Add(tool);

      var options = new ChatOptions
      {
         Tools = aiTools,
         Temperature = (float?)temperature
      };

      string responseText = string.Empty;
      var callCountBefore = calls.Count;

      for (var i = 0; i < iterations; i++)
      {
         var response = await client.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
         responseText = response.Text ?? string.Empty;

         // The function-invocation middleware has already run the tools by the time the
         // response comes back, so the audit trail is read off the message contents rather
         // than produced by invoking them again here.
         var contents = response.Messages.SelectMany(m => m.Contents).ToList();
         var resultsByCallId = contents
            .OfType<FunctionResultContent>()
            .Where(r => r.CallId is not null)
            .ToDictionary(r => r.CallId!, r => r, StringComparer.Ordinal);

         foreach (var call in contents.OfType<FunctionCallContent>())
         {
            var result = call.CallId is not null && resultsByCallId.TryGetValue(call.CallId, out var r) ? r : null;

            calls.Add(new ToolCallLog(
               member.DisplayName,
               call.Name,
               call.Arguments is null ? "{}" : JsonSerializer.Serialize(call.Arguments, ToolJson.Options),
               result?.Result?.ToString() ?? string.Empty,
               result?.Exception is null,
               result?.Exception?.Message,
               ToolCallTransport.Native,
               TimeSpan.Zero,
               roundNumber));
         }

         if (calls.Count == callCountBefore)
            break;

         callCountBefore = calls.Count;
         messages.AddRange(response.Messages);
      }

      return (responseText, calls);
   }

   /// <summary>
   ///    Marker protocol for providers that only return plain strings.
   /// </summary>
   private static async Task<(string Response, IReadOnlyList<ToolCallLog> Calls)> AskWithMarkersAsync(
      CouncilMember member,
      IReadOnlyList<AIFunction> tools,
      string systemPrompt,
      string userPrompt,
      float temperature,
      int maxIterations,
      int roundNumber,
      List<ToolCallLog> calls,
      CancellationToken ct)
   {
      var briefing = ToolCallParser.BuildBriefing(tools);
      var response = await member.AskAsync(systemPrompt + briefing, userPrompt, temperature, ct)
         .ConfigureAwait(false);

      var iterations = Math.Max(1, maxIterations);
      var madeCalls = 0;

      while (madeCalls < iterations)
      {
         var requests = ToolCallParser.Parse(response);
         if (requests.Count == 0) break;

         var sb = new StringBuilder();
         sb.AppendLine("Tool results:");
         foreach (var request in requests)
         {
            var log = await InvokeMarkerAsync(request, member, tools, roundNumber, ct).ConfigureAwait(false);
            calls.Add(log);
            madeCalls++;

            sb.AppendLine($"- `{log.ToolName}` → {(log.Succeeded ? log.Result : $"[error] {log.ErrorMessage}")}");

            // The marker is stripped so the next answer is not an echo of the request.
            response = response.Replace(request.Marker, string.Empty, StringComparison.Ordinal);
         }

         sb.AppendLine();
         sb.AppendLine("Using those results, give your final position. Do not repeat the tool markers.");

         response = await member.AskAsync(
            systemPrompt + briefing,
            userPrompt + "\n\n" + sb,
            temperature,
            ct).ConfigureAwait(false);
      }

      return (response, calls);
   }

   private static async Task<ToolCallLog> InvokeMarkerAsync(
      ToolCallParser.Request request,
      CouncilMember member,
      IReadOnlyList<AIFunction> tools,
      int roundNumber,
      CancellationToken ct)
   {
      var tool = tools.FirstOrDefault(t =>
         string.Equals(t.Name, request.ToolName, StringComparison.OrdinalIgnoreCase));

      if (tool is null)
      {
         return new ToolCallLog(
            member.DisplayName, request.ToolName, request.ArgumentsJson, string.Empty, false,
            $"No tool named '{request.ToolName}' is available.", ToolCallTransport.Marker, TimeSpan.Zero, roundNumber);
      }

      var sw = Stopwatch.StartNew();
      try
      {
         var arguments = new AIFunctionArguments(ParseArguments(request.ArgumentsJson));
         var result = await tool.InvokeAsync(arguments, ct).ConfigureAwait(false);
         sw.Stop();

         return new ToolCallLog(
            member.DisplayName, tool.Name, request.ArgumentsJson, result?.ToString() ?? string.Empty,
            true, null, ToolCallTransport.Marker, sw.Elapsed, roundNumber);
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (Exception ex)
      {
         sw.Stop();
         return new ToolCallLog(
            member.DisplayName, tool.Name, request.ArgumentsJson, string.Empty, false,
            ex.Message, ToolCallTransport.Marker, sw.Elapsed, roundNumber);
      }
   }

   /// <summary>
   ///    Parses a raw JSON argument object, tolerating the malformed payloads models routinely
   ///    emit. A bad payload produces an empty argument set rather than an exception, because a
   ///    member emitting nonsense is not a fault in the host.
   /// </summary>
   private static Dictionary<string, object?> ParseArguments(string? argumentsJson)
   {
      if (string.IsNullOrWhiteSpace(argumentsJson)) return [];

      try
      {
         return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson, ToolJson.Options) ?? [];
      }
      catch (JsonException)
      {
         return [];
      }
   }
}