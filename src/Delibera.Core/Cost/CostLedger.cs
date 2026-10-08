using Delibera.Core.Compression;

namespace Delibera.Core.Cost;

/// <summary>
///    Thread-safe accumulator of a single debate's spend.
/// </summary>
/// <remarks>
///    <para>
///       One instance is shared by reference across every round, exactly like
///       <see cref="DebateExecutionOptions.CompressionLogs" />: the fan-out collects member
///       responses concurrently, so each completed call has to be recorded from inside its own
///       task.
///    </para>
///    <para>
///       Prompt tokens are approximated, because the framework sends its own prompts and only the
///       provider sees the exact billed count. Completion tokens are counted from the response
///       text. Both are estimates; <see cref="CostEstimate.IsEstimate" /> marks the result so a
///       caller can tell a real invoice from a projection.
///    </para>
/// </remarks>
public sealed class CostLedger
{
   private readonly Lock _gate = new();
   private readonly Dictionary<string, MemberAccumulator> _members = new(StringComparer.Ordinal);
   private readonly IModelPricingRegistry? _registry;
   private bool _truncated;

   /// <summary>
   ///    Creates a ledger.
   /// </summary>
   /// <param name="registry">
   ///    Optional price list. When <c>null</c>, or when a model has no registered price, the
   ///    cost falls back to zero and <see cref="CostEstimate.IsEstimate" /> is set — a silent
   ///    zero would read as "this model was free".
   /// </param>
   public CostLedger(IModelPricingRegistry? registry = null)
   {
      _registry = registry;
   }

   /// <summary>Whether a cost gate stopped the debate before every planned call ran.</summary>
   public bool WasTruncated
   {
      get
      {
         lock (_gate)
         {
            return _truncated;
         }
      }
   }

   /// <summary>Records that a member call completed and bills it.</summary>
   /// <param name="memberName">Member display name.</param>
   /// <param name="provider">Provider name.</param>
   /// <param name="model">Model identifier.</param>
   /// <param name="promptText">Prompt that was sent, used for the prompt-token estimate.</param>
   /// <param name="completionText">Response text returned by the model.</param>
   public void RecordCall(
      string memberName,
      string provider,
      string model,
      string promptText,
      string completionText)
   {
      var promptTokens = TokenCounter.Default.EstimateTokens(promptText);
      var completionTokens = TokenCounter.Default.EstimateTokens(completionText);

      var cost = 0m;
      var isEstimate = true;

      if (_registry is not null && _registry.TryGetPricing(model, out var pricing))
      {
         isEstimate = !IsExactMatch(model, pricing);
         cost = promptTokens / 1_000_000m * pricing.InputPerMillionTokens
                + completionTokens / 1_000_000m * pricing.OutputPerMillionTokens;
      }

      lock (_gate)
      {
         var key = $"{memberName}|{provider}|{model}";
         if (!_members.TryGetValue(key, out var accumulator))
         {
            accumulator = new MemberAccumulator(memberName, provider, model);
            _members[key] = accumulator;
         }

         accumulator.CallCount++;
         accumulator.PromptTokens += promptTokens;
         accumulator.CompletionTokens += completionTokens;
         accumulator.Cost += cost;
         accumulator.IsEstimate |= isEstimate;
      }
   }

   /// <summary>Marks the debate as stopped by a cost gate.</summary>
   public void MarkTruncated()
   {
      lock (_gate)
      {
         _truncated = true;
      }
   }

   /// <summary>
   ///    Builds an immutable snapshot. Safe to call at any point, including while member tasks
   ///    are still recording.
   /// </summary>
   public CostEstimate Build()
   {
      lock (_gate)
      {
         var members = new List<MemberCostBreakdown>(_members.Count);
         var totalCost = 0m;
         var prompt = 0;
         var completion = 0;
         var isEstimate = false;

         foreach (var accumulator in _members.Values)
         {
            totalCost += accumulator.Cost;
            prompt += accumulator.PromptTokens;
            completion += accumulator.CompletionTokens;
            isEstimate |= accumulator.IsEstimate;
            members.Add(accumulator.ToBreakdown());
         }

         return new CostEstimate(
            totalCost,
            prompt,
            completion,
            members.OrderBy(m => m.MemberName, StringComparer.Ordinal).ToList(),
            isEstimate,
            _truncated);
      }
   }

   /// <summary>
   ///    Whether the registry matched the model exactly, as opposed to via a substring pattern
   ///    or its fallback price.
   /// </summary>
   private bool IsExactMatch(string model, ModelPricing pricing)
   {
      return string.Equals(model, pricing.ModelName, StringComparison.OrdinalIgnoreCase);
   }

   private sealed class MemberAccumulator(string memberName, string provider, string model)
   {
      public string MemberName { get; } = memberName;
      public string Provider { get; } = provider;
      public string Model { get; } = model;
      public int CallCount { get; set; }
      public int PromptTokens { get; set; }
      public int CompletionTokens { get; set; }
      public decimal Cost { get; set; }
      public bool IsEstimate { get; set; }

      public MemberCostBreakdown ToBreakdown()
      {
         return new MemberCostBreakdown(MemberName, Model, CallCount, PromptTokens, CompletionTokens, Cost, IsEstimate);
      }
   }
}
