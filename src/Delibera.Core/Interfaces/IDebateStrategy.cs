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
   /// <param name="operator">Operator micro-agent for tool/MCP delegation (may be <c>null</c>).</param>
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
      Operator? @operator,
      int maxRounds = 4,
      float temperature = 0.7f,
      Action<DebateRound>? onRoundCompleted = null,
      CancellationToken ct = default);

   /// <summary>
   ///    Executes the full debate cycle with an extra <paramref name="executionOptions" />
   ///    bundle (response-language directive, parallelism budget, <see cref="ILogger" />).
   /// </summary>
   /// <remarks>
   ///    The default implementation forwards to the legacy overload, ignoring
   ///    <paramref name="executionOptions" />. Concrete strategies shipped with Delibera
   ///    (<see cref="Debate.StandardDebate" />, <see cref="Debate.CritiqueDebate" />,
   ///    <see cref="Debate.ConsensusDebate" />) override this to honour the options.
   ///    Custom strategies only need to override this overload to participate in
   ///    language enforcement and parallelism tuning.
   /// </remarks>
   Task<DebateResult> ExecuteAsync(
      IReadOnlyList<CouncilMember> members,
      PromptContext context,
      CouncilMember? chairman,
      KnowledgeKeeper? knowledgeKeeper,
      Operator? @operator,
      DebateExecutionOptions executionOptions,
      int maxRounds = 4,
      float temperature = 0.7f,
      Action<DebateRound>? onRoundCompleted = null,
      CancellationToken ct = default)
   {
      return ExecuteAsync(members, context, chairman, knowledgeKeeper, @operator,
         maxRounds, temperature, onRoundCompleted, ct);
   }
}

/// <summary>
///    Convenience base interface used by Delibera strategies to declare an overload of
///    <c>IDebateStrategy.ExecuteAsync</c> that takes <see cref="DebateExecutionOptions" />.
///    Concrete strategies derive from <see cref="Debate.DebateScenario" />, which implements
///    this interface, so they can override the overload.
/// </summary>
public interface IDebateStrategyWithOptions : IDebateStrategy
{
   /// <summary>
   ///    Executes the debate with <paramref name="executionOptions" /> (response language,
   ///    parallelism budget, logger). Strategies override this to honour the options.
   /// </summary>
   new Task<DebateResult> ExecuteAsync(
      IReadOnlyList<CouncilMember> members,
      PromptContext context,
      CouncilMember? chairman,
      KnowledgeKeeper? knowledgeKeeper,
      Operator? @operator,
      DebateExecutionOptions executionOptions,
      int maxRounds = 4,
      float temperature = 0.7f,
      Action<DebateRound>? onRoundCompleted = null,
      CancellationToken ct = default);
}