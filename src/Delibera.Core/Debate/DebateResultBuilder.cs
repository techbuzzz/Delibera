using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
///    Centralised builder for <see cref="DebateResult" /> instances.
///    All debate strategies share this code path so the resulting shape stays
///    consistent across the framework.
/// </summary>
internal sealed class DebateResultBuilder(
   IDebateStrategy strategy,
   IReadOnlyList<CouncilMember> members,
   PromptContext context,
   CouncilMember? chairman,
   KnowledgeKeeper? knowledgeKeeper)
{
   private readonly List<DebateRound> _rounds = [];
   private string? _openingStatement;
   private string? _finalVerdict;
   private DateTime? _completedAt;

   public string StrategyName => strategy.StrategyName;
   public IReadOnlyList<DebateRound> Rounds => _rounds;

   public void SetOpeningStatement(string? statement) => _openingStatement = statement;

   public void AddRound(DebateRound round) => _rounds.Add(round);

   public void SetFinalVerdict(string? verdict) => _finalVerdict = verdict;

   public void MarkCompleted() => _completedAt = DateTime.UtcNow;

   public DebateResult Build() => new()
   {
      StrategyName = strategy.StrategyName,
      Context = context,
      Participants = members.Select(m => m.DisplayName).ToList(),
      ChairmanName = chairman?.DisplayName,
      KnowledgeKeeperName = knowledgeKeeper?.DisplayName,
      OpeningStatement = _openingStatement,
      Rounds = _rounds,
      FinalVerdict = _finalVerdict,
      CompletedAt = _completedAt
   };
}
