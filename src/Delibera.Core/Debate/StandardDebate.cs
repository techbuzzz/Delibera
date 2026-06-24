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
       Operator? @operator,
       int maxRounds = 4,
       float temperature = 0.7f,
       Action<DebateRound>? onRoundCompleted = null,
       CancellationToken ct = default)
    {
       return await ExecuteAsync(members, context, chairman, knowledgeKeeper, @operator,
          DebateExecutionOptions.Default, maxRounds, temperature, onRoundCompleted, ct);
    }

    /// <inheritdoc />
    public override async Task<DebateResult> ExecuteAsync(
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
       ArgumentNullException.ThrowIfNull(members);
       ArgumentNullException.ThrowIfNull(context);
       var builder = new DebateResultBuilder(this, members, context, chairman, knowledgeKeeper, @operator);
       var fullUserPrompt = context.GetFullUserPrompt();

       // Operator briefing is appended to participant system prompts so they know what tools exist.
       var operatorBriefing = BuildOperatorBriefing(@operator);
       var baseSystemPrompt = context.SystemPrompt + operatorBriefing;

      // Operator briefing is appended to participant system prompts so they know what tools exist.
      var operatorBriefing = BuildOperatorBriefing(@operator);
      var baseSystemPrompt = context.SystemPrompt + operatorBriefing;

      // ── Chairman opening ──
      if (chairman is not null)
         builder.SetOpeningStatement(await Chairman.OpenDebateAsync(
            chairman, context, members, StrategyName, maxRounds, temperature, ct));

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

       var r1Responses = await CollectResponsesAsync(members, baseSystemPrompt, r1Prompt, temperature, ct);
       // Operator: fulfil any [[OPERATOR: ...]] requests raised in round 1 (parallel, bounded).
       var r1Op = await ProcessOperatorRequestsAsync(@operator, r1Responses, executionOptions, ct);
      var round1 = CreateRound(1, "Initial Responses",
         "All models provide their initial answers.", r1Responses, r1Prompt, r1Ki, r1Op);
      builder.AddRound(round1);
      onRoundCompleted?.Invoke(round1);

      if (maxRounds < 2) return BuildAndComplete(builder);

      // ── Round 2: Knowledge Keeper contextual update ──
      var r2Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (_, ki) = await QueryKnowledgeForRoundAsync(
            knowledgeKeeper, context.UserPrompt, 2, builder.Rounds, ct);
         if (ki is not null) r2Ki.Add(ki);
      }

      // ══════════ Round 2: Critique ══════════
      var r1Text = FormatRoundResponses(round1);
      var r1OpText = FormatOperatorInteractions(r1Op);
      var r2System = $"""
                      {baseSystemPrompt}

                      You are now in critique mode. Critically analyse the responses from other council members.
                      Identify strengths, weaknesses, factual errors, logical gaps, and areas for improvement.
                      Be constructive but thorough.
                      """;
      var r2Prompt = $"""
                      Original question: {fullUserPrompt}

                      Initial responses:
                      {r1Text}
                      {(string.IsNullOrWhiteSpace(r1OpText) ? "" : $"\n{r1OpText}")}
                      Provide your detailed critique of each response.
                      """;

       var r2Responses = await CollectResponsesAsync(members, r2System, r2Prompt, temperature, ct);
       var r2Op = await ProcessOperatorRequestsAsync(@operator, r2Responses, executionOptions, ct);
      var round2 = CreateRound(2, "Critique",
         "Models critically analyse each other's responses.", r2Responses, r2Prompt, r2Ki, r2Op);
      builder.AddRound(round2);
      onRoundCompleted?.Invoke(round2);

      if (maxRounds < 3) return BuildAndComplete(builder);

      // ── Round 3: Knowledge Keeper contextual update ──
      var r3Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (_, ki) = await QueryKnowledgeForRoundAsync(
            knowledgeKeeper, context.UserPrompt, 3, builder.Rounds, ct);
         if (ki is not null) r3Ki.Add(ki);
      }

      // ══════════ Round 3: Improved Responses ══════════
      var r2Text = FormatRoundResponses(round2);
      var r2OpText = FormatOperatorInteractions(r2Op);
      var r3System = $"""
                      {baseSystemPrompt}

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
                      {(string.IsNullOrWhiteSpace(r1OpText) && string.IsNullOrWhiteSpace(r2OpText) ? "" : $"\n{r1OpText}\n{r2OpText}")}
                      Provide your final, improved answer.
                      """;

       var r3Responses = await CollectResponsesAsync(members, r3System, r3Prompt, temperature, ct);
       var r3Op = await ProcessOperatorRequestsAsync(@operator, r3Responses, executionOptions, ct);
      var round3 = CreateRound(3, "Final Improved Responses",
         "Models provide refined answers incorporating critiques.", r3Responses, r3Prompt, r3Ki, r3Op);
      builder.AddRound(round3);
      onRoundCompleted?.Invoke(round3);

      // ══════════ Round 4: Chairman Verdict ══════════
      if (maxRounds >= 4 && chairman is not null)
         try
         {
            var finalVerdict = await Chairman.SynthesizeVerdictAsync(
               chairman, context, builder.Rounds, knowledgeKeeper, temperature, ct);
            builder.SetFinalVerdict(finalVerdict);

            var round4 = CreateRound(4, "Chairman Verdict",
               "The Chairman synthesises the final verdict.",
               new Dictionary<string, string> { [chairman.DisplayName] = finalVerdict });
            builder.AddRound(round4);
            onRoundCompleted?.Invoke(round4);
         }
         catch (Exception ex)
         {
            builder.SetFinalVerdict($"[CHAIRMAN ERROR: {ex.Message}]");
         }

      return BuildAndComplete(builder);
   }

   private static DebateResult BuildAndComplete(DebateResultBuilder builder)
   {
      builder.MarkCompleted();
      return builder.Build();
   }
}