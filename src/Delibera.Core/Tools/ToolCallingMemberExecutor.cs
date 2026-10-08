using System.Runtime.CompilerServices;
using Delibera.Core.Extensions;
using Delibera.Core.Providers.LLM;

namespace Delibera.Core.Tools;

/// <summary>
///    Runs one member's turn, invoking tools when the model asks for them.
/// </summary>
/// <remarks>
///    <para>
///       There is deliberately no loop here. <c>FunctionInvokingChatClient</c> owns the
///       invoke-resume cycle, its iteration bound and its concurrency handling, and this library used
///       to carry a second copy of that loop — which is how the two drifted.
///    </para>
///    <para>
///       The member's provider is exposed through <c>AsChatClient()</c>. For a provider that already
///       wraps a real <see cref="IChatClient" /> that returns the underlying client untouched, so tool
///       requests travel as genuine structured function-call traffic. For a string-only provider the
///       adapter translates them into the <c>[[TOOL: …]]</c> text protocol and back, so the same
///       middleware drives the loop either way.
///    </para>
///    <para>
///       What this class still owns is the audit trail: <c>FunctionInvokingChatClient</c> invokes the
///       tools itself and does not report what it ran, and a debate result whose tool usage rests on
///       the model's own account is not evidence of anything.
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

      var inner = member.Provider.AsChatClient(member.ModelName);

      // Order matters. The builder applies factories in reverse, so the first one added becomes the
      // OUTERMOST layer. The recorder therefore goes last: it sits directly around the provider, so
      // it observes every round-trip the middleware makes. Placed outside, it would only ever see
      // the final answer and the audit trail would silently lose all but the last iteration — which
      // is exactly the gap a tool log exists to close.
      var recorder = new RecordingChatClient(inner, calls, member, roundNumber, tools);

      var client = inner
         .AsBuilder()
         .UseFunctionInvocation(
            null,
            options =>
            {
               // A model that keeps requesting tools must not keep the debate alive.
               options.MaximumIterationsPerRequest = Math.Max(1, maxIterations);
            })
         .Use(innerClient => recorder.Wrap(innerClient))
         .Build();

      var aiTools = new List<AITool>(tools.Count);
      foreach (var tool in tools) aiTools.Add(tool);

      var messages = new List<ChatMessage>
      {
         new(ChatRole.System, systemPrompt),
         new(ChatRole.User, userPrompt)
      };

      var options2 = new ChatOptions
      {
         Tools = aiTools,
         Temperature = temperature,
         ModelId = member.ModelName
      };

      var response = await client.GetResponseAsync(messages, options2, ct).ConfigureAwait(false);

      // Anything the loop never came back for is reported rather than dropped.
      recorder.FlushPending();

      // Whether the request reached the model as structured function-call content or as the
      // [[TOOL: …]] text protocol depends on the provider underneath. Both end up here, and a
      // reader comparing two runs needs to know which one was in play.
      NormaliseTransport(calls, member);

      return (response.Text ?? string.Empty, calls);
   }

   private static void NormaliseTransport(List<ToolCallLog> calls, CouncilMember member)
   {
      if (member.Provider is ChatClientLLMProvider) return;

      for (var i = 0; i < calls.Count; i++)
         calls[i] = calls[i] with { Transport = ToolCallTransport.Marker };
   }

   /// <summary>
   ///    Sits between the function-invocation middleware and the provider, recording every tool
   ///    call the model makes across every iteration of the loop.
   /// </summary>
   /// <remarks>
   ///    <para>
   ///       The middleware invokes the functions itself and does not report what it ran, so the audit
   ///       trail has to be observed from the transport side. Reading it off the middleware's final
   ///       response instead would report only the last iteration.
   ///    </para>
   ///    <para>
   ///       Results are paired by <c>CallId</c>, never by position: one turn can make several calls,
   ///       and positional pairing would attribute a result to whichever call happened to be listed
   ///       first.
   ///    </para>
   ///    <para>
   ///       The call and its result arrive in <em>different</em> round-trips. The request leaves here
   ///       as a <c>FunctionCallContent</c>; the tool is invoked afterwards, by the middleware, and the
   ///       result only comes back on the <em>next</em> request this client receives. So a call is
   ///       held pending until a matching result shows up in an inbound conversation, and anything
   ///       still pending when the loop ends is reported as having produced nothing. Reading the pair
   ///       out of a single response instead reports every call as failed — which is precisely the
   ///       wrong answer for the only case worth auditing.
   ///    </para>
   /// </remarks>
   private sealed class RecordingChatClient(
      IChatClient inner,
      List<ToolCallLog> sink,
      CouncilMember member,
      int roundNumber,
      IReadOnlyList<AIFunction> catalogue) : IChatClient
   {
      private readonly Dictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);

      public async Task<ChatResponse> GetResponseAsync(
         IEnumerable<ChatMessage> messages,
         ChatOptions? options = null,
         CancellationToken cancellationToken = default)
      {
         // Settle first: this request carries the results of the previous round's calls.
         Settle(messages);

         var response = await inner.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
         if (response.Messages is not null)
            Track(response.Messages.SelectMany(m => m.Contents));
         return response;
      }

      public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
         IEnumerable<ChatMessage> messages,
         ChatOptions? options = null,
         [EnumeratorCancellation] CancellationToken cancellationToken = default)
      {
         Settle(messages);

         await foreach (var update in inner
                           .GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
         {
            Track(update.Contents);
            yield return update;
         }
      }

      public object? GetService(Type serviceType, object? serviceKey = null)
      {
         return inner.GetService(serviceType, serviceKey);
      }

      public void Dispose()
      {
         inner.Dispose();
      }

      /// <summary>Hands the builder's inner client to the recording layer.</summary>
      /// <param name="client">The client the middleware wraps.</param>
      public IChatClient Wrap(IChatClient client)
      {
         inner = client;
         return this;
      }

      /// <summary>
      ///    Emits a log entry for every call that never received a result. Called once the
      ///    middleware has returned, so a call the loop abandoned is not silently missing.
      /// </summary>
      public void FlushPending()
      {
         foreach (var pending in _pending.Values) sink.Add(Build(pending, null));

         _pending.Clear();
      }

      /// <summary>Matches results in this request against calls still awaiting one.</summary>
      private void Settle(IEnumerable<ChatMessage> messages)
      {
         var results = messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Where(r => r.CallId is not null)
            .GroupBy(r => r.CallId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

         if (results.Count == 0) return;

         foreach (var (callId, result) in results)
         {
            if (!_pending.Remove(callId, out var pending)) continue;
            sink.Add(Build(pending, result));
         }
      }

      /// <summary>Holds new calls until their results arrive on a later round-trip.</summary>
      private void Track(IEnumerable<AIContent> contents)
      {
         foreach (var call in contents.OfType<FunctionCallContent>())
         {
            // Informational-only calls are annotations on the model output, not invocations.
            if (call.InformationalOnly) continue;
            if (call.CallId is not { Length: > 0 } callId) continue;
            if (_pending.ContainsKey(callId)) continue;

            _pending[callId] = new PendingCall(call.Name, ToolCallParser.SerializeArguments(call.Arguments));
         }
      }

      private ToolCallLog Build(PendingCall pending, FunctionResultContent? result)
      {
         // The middleware ignores a call it cannot resolve, and it may report that back either as an
         // exception or as an error value in the payload. Relying on the exception alone therefore
         // reports an unknown tool as a success. What is unambiguous is the catalogue: a name that
         // was never offered cannot have run, whatever came back.
         var known = catalogue.Any(t => string.Equals(t.Name, pending.Name, StringComparison.OrdinalIgnoreCase));
         var succeeded = result is not null && result.Exception is null && known;

         var failure = result?.Exception?.Message
                       ?? (known
                          ? "The tool produced no result."
                          : $"No tool named '{pending.Name}' is available.");

         return new ToolCallLog(
            member.DisplayName,
            pending.Name,
            pending.Arguments,
            result?.Result?.ToString() ?? string.Empty,
            succeeded,
            succeeded ? null : failure,
            ToolCallTransport.Native,
            TimeSpan.Zero,
            roundNumber);
      }

      private readonly record struct PendingCall(string Name, string Arguments);
   }
}
