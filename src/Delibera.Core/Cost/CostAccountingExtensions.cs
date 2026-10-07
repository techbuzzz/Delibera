using Delibera.Core.Models;

namespace Delibera.Core.Cost;

/// <summary>
///    Cost-accounting helpers used by the debate pipeline.
/// </summary>
public static class CostAccountingExtensions
{
   /// <summary>
   ///    Records one completed member call against the run's shared ledger.
   /// </summary>
   /// <param name="options">Execution options carrying the ledger; <c>null</c> is a no-op.</param>
   /// <param name="member">The member that made the call.</param>
   /// <param name="systemPrompt">System prompt sent to the model.</param>
   /// <param name="userPrompt">User prompt sent to the model.</param>
   /// <param name="response">Text the model returned.</param>
   public static void RecordMemberCall(
      this DebateExecutionOptions? options,
      CouncilMember member,
      string systemPrompt,
      string userPrompt,
      string response)
   {
      if (options is null) return;

      var prompt = string.IsNullOrEmpty(systemPrompt) ? userPrompt : systemPrompt + "\n" + userPrompt;
      options.GetOrCreateLedger().RecordCall(
         member.DisplayName,
         member.Provider.ProviderName,
         member.ModelName,
         prompt,
         response);
   }
}