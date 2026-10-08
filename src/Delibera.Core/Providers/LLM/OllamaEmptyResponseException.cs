namespace Delibera.Core.Providers.LLM;

/// <summary>
///    Thrown when a model completed its stream without producing any answer text.
/// </summary>
/// <remarks>
///    <para>
///       Replaces the previous bare
///       <c>
///          InvalidOperationException("Empty response from model
///          '...'")
///       </c>
///       . On a reasoning model an empty response is almost never a bug in the
///       request: the model spends the generation budget thinking, is cut off at the limit, and
///       reports <c>done_reason: length</c> with zero content. The two most common shapes are
///       therefore distinguished explicitly:
///    </para>
///    <list type="bullet">
///       <item>
///          <description>
///             <see cref="ReasoningChars" /> &gt; 0 and <see cref="DoneReason" /> is
///             <c>length</c> — the budget went into reasoning. Raise
///             <c>maxOutputTokens</c>, or construct the provider with <c>enableThinking: false</c> so
///             the whole budget is available for the answer.
///          </description>
///       </item>
///       <item>
///          <description>
///             <see cref="ReasoningChars" /> is 0 — the model returned nothing at all, which usually
///             means the model id is wrong, the model is not enabled for the account, or the request
///             was rejected silently by the endpoint.
///          </description>
///       </item>
///    </list>
/// </remarks>
public sealed class OllamaEmptyResponseException : InvalidOperationException
{
   /// <summary>Creates the exception for a model that returned no answer.</summary>
   /// <param name="model">Model id that produced the empty response.</param>
   /// <param name="doneReason">Server-reported finish reason, when available.</param>
   /// <param name="reasoningChars">Length of the reasoning text the model produced, if any.</param>
   public OllamaEmptyResponseException(string model, string? doneReason, int reasoningChars)
      : base(BuildMessage(model, doneReason, reasoningChars))
   {
      Model = model;
      DoneReason = doneReason;
      ReasoningChars = reasoningChars;
   }

   /// <summary>Model id that produced the empty response.</summary>
   public string Model { get; }

   /// <summary>Server-reported finish reason, typically <c>length</c> or <c>stop</c>.</summary>
   public string? DoneReason { get; }

   /// <summary>Length of the reasoning text produced before the answer slot stayed empty.</summary>
   public int ReasoningChars { get; }

   /// <summary>
   ///    <c>true</c> when the generation budget was consumed by reasoning before the model could
   ///    emit an answer.
   /// </summary>
   public bool BudgetConsumedByReasoning =>
      ReasoningChars > 0 && string.Equals(DoneReason, "length", StringComparison.OrdinalIgnoreCase);

   private static string BuildMessage(string model, string? doneReason, int reasoningChars)
   {
      var reason = doneReason is { Length: > 0 } ? doneReason : "unspecified";

      if (reasoningChars > 0)
         return $"Empty response from model '{model}': the model produced {reasoningChars:N0} characters of " +
                $"reasoning and was cut off (done_reason '{reason}') before emitting an answer. " +
                "Raise maxOutputTokens, or construct the provider with enableThinking: false so the whole " +
                "generation budget is available for the answer.";

      return $"Empty response from model '{model}' (done_reason '{reason}'). The model returned no answer and " +
             "no reasoning; verify the model id is enabled for this account.";
   }
}
