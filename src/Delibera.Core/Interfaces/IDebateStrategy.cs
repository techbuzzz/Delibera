using Delibera.Core.Council;

namespace Delibera.Core.Interfaces;

/// <summary>
///    Defines the debate flow — round order, interaction rules and verdict formation.
///    Implement to create new debate scenarios.
/// </summary>
public interface IDebateStrategy
{
   /// <summary>Unique strategy name.</summary>
   string StrategyName { get; }

   /// <summary>Human-readable strategy description.</summary>
   string Description { get; }

   /// <summary>
   ///    Executes the full debate cycle according to this strategy.
   /// </summary>
   /// <param name="members">Council participants.</param>
   /// <param name="context">Prompt context (system / user prompt, knowledge).</param>
   /// <param name="chairman">Chairman for moderation and verdict synthesis (may be <c>null</c>).</param>
   /// <param name="knowledgeKeeper">Knowledge Keeper for RAG queries (may be <c>null</c>).</param>
   /// <param name="maxRounds">Maximum number of rounds.</param>
   /// <param name="temperature">Generation temperature.</param>
   /// <param name="onRoundCompleted">Callback invoked after each round.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Complete debate result.</returns>
   Task<DebateResult> ExecuteAsync(
      IReadOnlyList<CouncilMember> members,
      PromptContext context,
      CouncilMember? chairman,
      KnowledgeKeeper? knowledgeKeeper,
      int maxRounds = 4,
      float temperature = 0.7f,
      Action<DebateRound>? onRoundCompleted = null,
      CancellationToken ct = default);
}