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
   KnowledgeKeeper? knowledgeKeeper,
   Operator? @operator = null)
{
   private readonly List<DebateRound> _rounds = [];

   // Captured when the builder is created, which is when the debate starts. DebateResult.StartedAt
   // used to rely on its property initialiser, which runs inside Build() — after MarkCompleted() has
   // already stamped CompletedAt — so TotalDuration came out negative ("-0.0s") on every debate.
   private readonly DateTime _startedAt = DateTime.UtcNow;
   private DateTime? _completedAt;
   private IReadOnlyList<MemberFailure> _failures = [];
   private string? _finalVerdict;
   private string? _openingStatement;

   public string StrategyName => strategy.StrategyName;
   public PromptContext Context => context;
   public IReadOnlyList<DebateRound> Rounds => _rounds;

   public void SetOpeningStatement(string? statement)
   {
      _openingStatement = statement;
   }

   public void AddRound(DebateRound round)
   {
      _rounds.Add(round);
   }

   public void SetFinalVerdict(string? verdict)
   {
      _finalVerdict = verdict;
   }

   /// <summary>Records members that failed during the debate so the result can report degradation.</summary>
   public void WithFailures(IReadOnlyList<MemberFailure> failures)
   {
      _failures = failures;
   }

   public void MarkCompleted()
   {
      _completedAt = DateTime.UtcNow;
   }

   public DebateResult Build()
   {
      return new DebateResult
      {
         StrategyName = strategy.StrategyName,
         Context = context,
         Participants = members.Select(m => m.DisplayName).ToList(),
         ChairmanName = chairman?.DisplayName,
         KnowledgeKeeperName = knowledgeKeeper?.DisplayName,
         OperatorName = @operator?.DisplayName,
         OpeningStatement = _openingStatement,
         Rounds = _rounds,
         FinalVerdict = _finalVerdict,
         StartedAt = _startedAt,
         CompletedAt = _completedAt,
         FailedMembers = _failures
      };
   }
}
