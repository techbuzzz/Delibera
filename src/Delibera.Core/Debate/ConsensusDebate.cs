using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
///    Collaborative debate aiming for consensus:
///    1. Initial Perspectives
///    2. Finding Common Ground
///    3. Consensus Building
///    4. Chairman Facilitator — documents the consensus outcome
/// </summary>
/// <remarks>
///    <para>v3.0: Enhanced with per-round Knowledge Keeper queries before each round.</para>
/// </remarks>
public sealed class ConsensusDebate : DebateScenario
{
   /// <inheritdoc />
   public override string StrategyName => "Consensus Debate";

   /// <inheritdoc />
   public override string Description => "Collaborative debate: Perspectives → Common Ground → Consensus → Facilitator";

   /// <inheritdoc />
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

      var enrichedPrompt = string.IsNullOrWhiteSpace(knowledgeCtx)
         ? fullUserPrompt
         : $"{fullUserPrompt}\n\n📚 Knowledge:\n{knowledgeCtx}";

      // Round 1: Initial Perspectives
      var r1 = await CollectResponsesAsync(members, context.SystemPrompt, enrichedPrompt, temperature, ct);
      var round1 = CreateRound(1, "Initial Perspectives", "Each model shares their perspective.", r1, knowledgeInteractions: r1Ki);
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

      // Round 2: Finding Common Ground
      var r1Text = FormatRoundResponses(round1);
      var r2Sys = $"""
                   {context.SystemPrompt}
                   Work collaboratively. Find COMMON GROUND. Identify agreements, disagreements,
                   and propose bridges. Be open to changing your position.
                   """;
      var r2Prompt = $"""
                      Original question: {fullUserPrompt}
                      All perspectives:
                      {r1Text}
                      Identify: 1) Points of Agreement 2) Points of Disagreement 3) Bridge Proposals 4) Your Updated Position
                      """;
      var r2 = await CollectResponsesAsync(members, r2Sys, r2Prompt, temperature, ct);
      var round2 = CreateRound(2, "Finding Common Ground", "Models identify agreements and disagreements.", r2, knowledgeInteractions: r2Ki);
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

      // Round 3: Consensus Building
      var r2Text = FormatRoundResponses(round2);
      var r3Sys = $"""
                   {context.SystemPrompt}
                   This is the CONSENSUS round. Formulate a single, unified answer.
                   If full consensus is impossible, state what can be agreed upon.
                   """;
      var r3Prompt = $"""
                      Original question: {fullUserPrompt}
                      Perspectives: {r1Text}
                      Common ground: {r2Text}
                      Formulate: 1) Agreed points 2) Proposed unified answer 3) Remaining disagreements 4) Confidence (Low/Medium/High)
                      """;
      var r3 = await CollectResponsesAsync(members, r3Sys, r3Prompt, temperature, ct);
      var round3 = CreateRound(3, "Consensus Building", "Models attempt a unified answer.", r3, knowledgeInteractions: r3Ki);
      rounds.Add(round3);
      onRoundCompleted?.Invoke(round3);

      // Round 4: Chairman Facilitator
      string? verdict = null;
      if (maxRounds >= 4 && chairman is not null)
         try
         {
            verdict = await Chairman.SynthesizeVerdictAsync(chairman, context, rounds, knowledgeKeeper, temperature, ct);
            var round4 = CreateRound(4, "Consensus Facilitator", "The Chairman documents the consensus outcome.",
               new Dictionary<string, string> { [chairman.DisplayName] = verdict });
            rounds.Add(round4);
            onRoundCompleted?.Invoke(round4);
         }
         catch (Exception ex)
         {
            verdict = $"[FACILITATOR ERROR: {ex.Message}]";
         }

      return Build(verdict);

      DebateResult Build(string? v = null)
      {
         return new DebateResult
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
}