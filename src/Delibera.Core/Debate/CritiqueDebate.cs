using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
/// Adversarial debate with deep critique and defence:
/// 1. Initial Positions
/// 2. Directed Critique
/// 3. Defence &amp; Counter-Arguments
/// 4. Chairman Judge Verdict
/// </summary>
/// <remarks>
/// <para>v3.0: Enhanced with per-round Knowledge Keeper queries before each round.</para>
/// </remarks>
public sealed class CritiqueDebate : DebateScenario
{
   /// <inheritdoc/>
   public override string StrategyName => "Critique Debate";

   /// <inheritdoc/>
   public override string Description => "Adversarial debate: Position → Critique → Defence → Judge Verdict";

   /// <inheritdoc/>
   public override async Task<DebateResult> ExecuteAsync(
       IReadOnlyList<CouncilMember> members,
       PromptContext context,
       CouncilMember? chairman,
       KnowledgeKeeper? knowledgeKeeper,
       int maxRounds = 4,
       float temperature = 0.7f,
       Action<DebateRound>? onRoundCompleted = null,
       CancellationToken ct = default)
   {
      var rounds = new List<DebateRound>();
      var fullUserPrompt = context.GetFullUserPrompt();

      string? openingStatement = null;
      if (chairman is not null)
         openingStatement = await Chairman.OpenDebateAsync(chairman, context, members, StrategyName, maxRounds, temperature, ct);

      // Round 1: Knowledge pre-research
      var r1Ki = new List<KnowledgeInteraction>();
      var knowledgeCtx = "";
      if (knowledgeKeeper is not null)
      {
         var (ctx, ki) = await QueryKnowledgeForRoundAsync(knowledgeKeeper, context.UserPrompt, 1, ct: ct);
         knowledgeCtx = ctx;
         if (ki is not null) r1Ki.Add(ki);
      }

      var enrichedPrompt = string.IsNullOrWhiteSpace(knowledgeCtx) ? fullUserPrompt : $"{fullUserPrompt}\n\n📚 Knowledge:\n{knowledgeCtx}";

      // Round 1: Initial Positions
      var r1 = await CollectResponsesAsync(members, context.SystemPrompt, enrichedPrompt, temperature, ct);
      var round1 = CreateRound(1, "Initial Positions", "Each model states their initial position.", r1, knowledgeInteractions: r1Ki);
      rounds.Add(round1);
      onRoundCompleted?.Invoke(round1);
      if (maxRounds < 2) return Build();

      // Round 2: KK update
      var r2Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (_, ki) = await QueryKnowledgeForRoundAsync(knowledgeKeeper, context.UserPrompt, 2, rounds, ct);
         if (ki is not null) r2Ki.Add(ki);
      }

      // Round 2: Directed Critique
      var r1Text = FormatRoundResponses(round1);
      var r2Sys = $"""
            {context.SystemPrompt}
            You are a sharp analytical critic. Find the WEAKEST points in other participants' arguments.
            Challenge assumptions, find logical flaws, identify missing evidence, propose counter-examples.
            """;
      var r2Prompt = $"""
            Original question: {fullUserPrompt}
            Responses to critique:
            {r1Text}
            For each response: 1) Weakest argument 2) Logical fallacies 3) Counter-example 4) Quality (1-10)
            """;
      var r2 = await CollectResponsesAsync(members, r2Sys, r2Prompt, temperature, ct);
      var round2 = CreateRound(2, "Directed Critique", "Models attack weaknesses in each other's positions.", r2, knowledgeInteractions: r2Ki);
      rounds.Add(round2);
      onRoundCompleted?.Invoke(round2);
      if (maxRounds < 3) return Build();

      // Round 3: KK update
      var r3Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (_, ki) = await QueryKnowledgeForRoundAsync(knowledgeKeeper, context.UserPrompt, 3, rounds, ct);
         if (ki is not null) r3Ki.Add(ki);
      }

      // Round 3: Defence
      var r2Text = FormatRoundResponses(round2);
      var r3Sys = $"""
            {context.SystemPrompt}
            Defend your position. Address every critique, strengthen weak arguments,
            provide additional evidence, and concede where critics are right.
            """;
      var r3Prompt = $"""
            Original question: {fullUserPrompt}
            Initial responses: {r1Text}
            Critiques: {r2Text}
            Defend your position: 1) Address each criticism 2) Strengthen weak points 3) Concede where right 4) Final answer
            """;
      var r3 = await CollectResponsesAsync(members, r3Sys, r3Prompt, temperature, ct);
      var round3 = CreateRound(3, "Defence & Counter-Arguments", "Models defend their positions.", r3, knowledgeInteractions: r3Ki);
      rounds.Add(round3);
      onRoundCompleted?.Invoke(round3);

      // Round 4: Judge verdict
      string? verdict = null;
      if (maxRounds >= 4 && chairman is not null)
      {
         try
         {
            verdict = await Chairman.SynthesizeVerdictAsync(chairman, context, rounds, knowledgeKeeper, temperature, ct);
            var round4 = CreateRound(4, "Judge's Verdict", "The Chairman judges the debate.",
                new Dictionary<string, string> { [chairman.DisplayName] = verdict });
            rounds.Add(round4);
            onRoundCompleted?.Invoke(round4);
         }
         catch (Exception ex) { verdict = $"[JUDGE ERROR: {ex.Message}]"; }
      }

      return Build(verdict);

      DebateResult Build(string? v = null) => new()
      {
         StrategyName = StrategyName,
         Context = context,
         Participants = members.Select(m => m.DisplayName).ToList(),
         ChairmanName = chairman?.DisplayName,
         KnowledgeKeeperName = knowledgeKeeper?.DisplayName,
         OpeningStatement = openingStatement,
         Rounds = rounds,
         FinalVerdict = v,
         CompletedAt = DateTime.UtcNow
      };
   }
}
