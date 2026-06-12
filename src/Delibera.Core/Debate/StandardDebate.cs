using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
///    Standard 4-round debate:
///    1. Initial Responses — all models answer the question.
///    2. Critique — models critique each other's answers.
///    3. Improved Responses — models refine their answers.
///    4. Chairman Verdict — the Chairman synthesises the final verdict.
/// </summary>
/// <remarks>
///    <para>
///       v3.0: Enhanced with per-round Knowledge Keeper queries.
///       The Chairman auto-queries the Knowledge Keeper before each round to provide
///       structured, fact-based context with sources.
///    </para>
/// </remarks>
public sealed class StandardDebate : DebateScenario
{
   /// <inheritdoc />
   public override string StrategyName => "Standard Debate";

   /// <inheritdoc />
   public override string Description => "4-round debate: Initial → Critique → Improved → Chairman Verdict";

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

      // ── Chairman opening ──
      string? openingStatement = null;
      if (chairman is not null)
         openingStatement = await Chairman.OpenDebateAsync(
            chairman, context, members, StrategyName, maxRounds, temperature, ct);

      // ── Round 1: Knowledge Keeper pre-research ──
      var r1Ki = new List<KnowledgeInteraction>();
      var knowledgeContext = "";
      if (knowledgeKeeper is not null)
      {
         var (ctx, ki) = await QueryKnowledgeForRoundAsync(knowledgeKeeper, context.UserPrompt, 1, ct: ct);
         knowledgeContext = ctx;
         if (ki is not null) r1Ki.Add(ki);
      }

      // ══════════ Round 1: Initial Responses ══════════
      var r1Prompt = string.IsNullOrWhiteSpace(knowledgeContext)
         ? fullUserPrompt
         : $"{fullUserPrompt}\n\n📚 Knowledge Keeper context:\n{knowledgeContext}";

      var r1Responses = await CollectResponsesAsync(members, context.SystemPrompt, r1Prompt, temperature, ct);
      var round1 = CreateRound(1, "Initial Responses",
         "All models provide their initial answers.", r1Responses, r1Prompt, r1Ki);
      rounds.Add(round1);
      onRoundCompleted?.Invoke(round1);

      if (maxRounds < 2) return BuildResult();

      // ── Round 2: Knowledge Keeper contextual update ──
      var r2Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (ctx, ki) = await QueryKnowledgeForRoundAsync(
            knowledgeKeeper, context.UserPrompt, 2, rounds, ct);
         if (ki is not null) r2Ki.Add(ki);
      }

      // ══════════ Round 2: Critique ══════════
      var r1Text = FormatRoundResponses(round1);
      var r2System = $"""
                      {context.SystemPrompt}

                      You are now in critique mode. Critically analyse the responses from other council members.
                      Identify strengths, weaknesses, factual errors, logical gaps, and areas for improvement.
                      Be constructive but thorough.
                      """;
      var r2Prompt = $"""
                      Original question: {fullUserPrompt}

                      Initial responses:
                      {r1Text}

                      Provide your detailed critique of each response.
                      """;

      var r2Responses = await CollectResponsesAsync(members, r2System, r2Prompt, temperature, ct);
      var round2 = CreateRound(2, "Critique",
         "Models critically analyse each other's responses.", r2Responses, r2Prompt, r2Ki);
      rounds.Add(round2);
      onRoundCompleted?.Invoke(round2);

      if (maxRounds < 3) return BuildResult();

      // ── Round 3: Knowledge Keeper contextual update ──
      var r3Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (ctx, ki) = await QueryKnowledgeForRoundAsync(
            knowledgeKeeper, context.UserPrompt, 3, rounds, ct);
         if (ki is not null) r3Ki.Add(ki);
      }

      // ══════════ Round 3: Improved Responses ══════════
      var r2Text = FormatRoundResponses(round2);
      var r3System = $"""
                      {context.SystemPrompt}

                      You have received critiques. Provide your FINAL, IMPROVED answer.
                      Take the best ideas from all participants, address the critiques,
                      and synthesise the most comprehensive response possible.
                      """;
      var r3Prompt = $"""
                      Original question: {fullUserPrompt}

                      Initial responses:
                      {r1Text}

                      Critiques:
                      {r2Text}

                      Provide your final, improved answer.
                      """;

      var r3Responses = await CollectResponsesAsync(members, r3System, r3Prompt, temperature, ct);
      var round3 = CreateRound(3, "Final Improved Responses",
         "Models provide refined answers incorporating critiques.", r3Responses, r3Prompt, r3Ki);
      rounds.Add(round3);
      onRoundCompleted?.Invoke(round3);

      // ══════════ Round 4: Chairman Verdict ══════════
      string? finalVerdict = null;
      if (maxRounds >= 4 && chairman is not null)
         try
         {
            finalVerdict = await Chairman.SynthesizeVerdictAsync(
               chairman, context, rounds, knowledgeKeeper, temperature, ct);

            var round4 = CreateRound(4, "Chairman Verdict",
               "The Chairman synthesises the final verdict.",
               new Dictionary<string, string> { [chairman.DisplayName] = finalVerdict });
            rounds.Add(round4);
            onRoundCompleted?.Invoke(round4);
         }
         catch (Exception ex)
         {
            finalVerdict = $"[CHAIRMAN ERROR: {ex.Message}]";
         }

      return BuildResult(finalVerdict);

      // ── local helper ──
      DebateResult BuildResult(string? verdict = null)
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
            FinalVerdict = verdict,
            CompletedAt = DateTime.UtcNow
         };
      }
   }
}