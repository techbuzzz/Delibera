namespace Delibera.Core.Models;

/// <summary>
///    What a debate cost, and per member where the money went.
/// </summary>
/// <param name="TotalCost">Total spend in the caller's currency unit (USD by convention).</param>
/// <param name="TotalPromptTokens">Prompt tokens billed across every member call.</param>
/// <param name="TotalCompletionTokens">Completion tokens billed across every member call.</param>
/// <param name="Members">Per-member breakdown.</param>
/// <param name="IsEstimate">
///    <c>true</c> when at least one member's model had no registered price, so the total was
///    computed from a fallback rate. A caller treating this as an invoice will be wrong.
/// </param>
/// <param name="WasTruncated">
///    <c>true</c> when a cost gate stopped the debate before every planned member call ran.
/// </param>
public sealed record CostEstimate(
   decimal TotalCost,
   int TotalPromptTokens,
   int TotalCompletionTokens,
   IReadOnlyList<MemberCostBreakdown> Members,
   bool IsEstimate,
   bool WasTruncated)
{
   /// <summary>An empty estimate — no calls were billed.</summary>
   public static CostEstimate Empty { get; } = new(
      0m, 0, 0, [], IsEstimate: false, WasTruncated: false);

   /// <summary>Average spend per billed member call.</summary>
   public decimal AveragePerMember => Members.Count == 0 ? 0m : TotalCost / Members.Count;
}

/// <summary>
///    One member's share of a debate's cost.
/// </summary>
/// <param name="MemberName">Display name of the member.</param>
/// <param name="ModelName">Model that produced the responses.</param>
/// <param name="CallCount">Number of billed model calls this member made.</param>
/// <param name="PromptTokens">Prompt tokens billed.</param>
/// <param name="CompletionTokens">Completion tokens billed.</param>
/// <param name="Cost">Spend attributed to this member.</param>
/// <param name="IsEstimate">
///    <c>true</c> when no price was registered for <paramref name="ModelName" /> and the cost
///    was derived from a fallback rate.
/// </param>
public sealed record MemberCostBreakdown(
   string MemberName,
   string ModelName,
   int CallCount,
   int PromptTokens,
   int CompletionTokens,
   decimal Cost,
   bool IsEstimate);

/// <summary>
///    Price list for one model, expressed per million tokens.
/// </summary>
/// <param name="ModelName">Model this price applies to.</param>
/// <param name="InputPerMillionTokens">Price per million prompt tokens.</param>
/// <param name="OutputPerMillionTokens">Price per million completion tokens.</param>
public sealed record ModelPricing(
   string ModelName,
   decimal InputPerMillionTokens,
   decimal OutputPerMillionTokens);