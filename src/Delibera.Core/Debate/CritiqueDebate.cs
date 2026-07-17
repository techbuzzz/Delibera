using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
///    Adversarial debate with deep critique and defence:
///    1. Initial Positions
///    2. Directed Critique
///    3. Defence &amp; Counter-Arguments
///    4. Chairman Judge Verdict
/// </summary>
/// <remarks>
///    <para>v3.0: Enhanced with per-round Knowledge Keeper queries before each round.</para>
/// </remarks>
public sealed class CritiqueDebate : DebateScenario
{
   /// <inheritdoc />
   public override string StrategyName => "Critique Debate";

   /// <inheritdoc />
   public override string Description => "Adversarial debate: Position → Critique → Defence → Judge Verdict";

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
      var totalRounds = Math.Min(maxRounds, 4);

      // Operator briefing appended to participant system prompts.
      var operatorBriefing = BuildOperatorBriefing(@operator);
      var baseSystemPrompt = context.SystemPrompt + operatorBriefing;

      if (chairman is not null)
         builder.SetOpeningStatement(await Chairman.OpenDebateAsync(chairman, context, members, StrategyName, maxRounds, temperature, ct).ConfigureAwait(false));

      // Round 1: Knowledge pre-research
      var r1Ki = new List<KnowledgeInteraction>();
      var knowledgeCtx = "";
      if (knowledgeKeeper is not null)
      {
         var (ctx, ki) = await QueryKnowledgeForRoundAsync(knowledgeKeeper, context.UserPrompt, 1, ct: ct).ConfigureAwait(false);
         knowledgeCtx = ctx;
         if (ki is not null) r1Ki.Add(ki);
      }

      var r1BasePrompt = BuildChunkedPrompt(context, 1, totalRounds);
      var enrichedPrompt = string.IsNullOrWhiteSpace(knowledgeCtx)
         ? r1BasePrompt
         : $"{r1BasePrompt}\n\n📚 Knowledge:\n{knowledgeCtx}";

      // Round 1: Initial Positions
      var round1StartedAt = DateTime.UtcNow;
       var r1 = await CollectResponsesAsync(members, baseSystemPrompt, enrichedPrompt, temperature, ct).ConfigureAwait(false);
       var r1Op = await ProcessOperatorRequestsAsync(@operator, r1, executionOptions, ct).ConfigureAwait(false);
      var round1 = CreateRound(1, "Initial Positions", "Each model states their initial position.", r1, knowledgeInteractions: r1Ki, operatorInteractions: r1Op, startedAt: round1StartedAt);
      builder.AddRound(round1);
      onRoundCompleted?.Invoke(round1);
       if (maxRounds < 2) return await FinalizeAsync(builder, chairman, knowledgeKeeper, temperature, onRoundCompleted, ct).ConfigureAwait(false);

      // Round 2: KK update
      var r2Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (_, ki) = await QueryKnowledgeForRoundAsync(knowledgeKeeper, context.UserPrompt, 2, builder.Rounds, ct).ConfigureAwait(false);
         if (ki is not null) r2Ki.Add(ki);
      }

      // Round 2: Directed Critique
      var r1Text = FormatRoundResponses(round1);
      var r1OpText = FormatOperatorInteractions(r1Op);
      var r2BasePrompt = BuildChunkedPrompt(context, 2, totalRounds, builder.Rounds);
      var r2Sys = $"""
                   {baseSystemPrompt}
                   You are a sharp analytical critic. Find the WEAKEST points in other participants' arguments.
                   Challenge assumptions, find logical flaws, identify missing evidence, propose counter-examples.
                   """;
      var r2Prompt = $"""
                      Original question: {r2BasePrompt}
                      Responses to critique:
                      {r1Text}
                      {(string.IsNullOrWhiteSpace(r1OpText) ? "" : $"\n{r1OpText}")}
                      For each response: 1) Weakest argument 2) Logical fallacies 3) Counter-example 4) Quality (1-10)
                      """;
      var round2StartedAt = DateTime.UtcNow;
       var r2 = await CollectResponsesAsync(members, r2Sys, r2Prompt, temperature, ct).ConfigureAwait(false);
       var r2Op = await ProcessOperatorRequestsAsync(@operator, r2, executionOptions, ct).ConfigureAwait(false);
      var round2 = CreateRound(2, "Directed Critique", "Models attack weaknesses in each other's positions.", r2, knowledgeInteractions: r2Ki, operatorInteractions: r2Op, startedAt: round2StartedAt);
      builder.AddRound(round2);
      onRoundCompleted?.Invoke(round2);
       if (maxRounds < 3) return await FinalizeAsync(builder, chairman, knowledgeKeeper, temperature, onRoundCompleted, ct).ConfigureAwait(false);

      // Round 3: KK update
      var r3Ki = new List<KnowledgeInteraction>();
      if (knowledgeKeeper is not null)
      {
         var (_, ki) = await QueryKnowledgeForRoundAsync(knowledgeKeeper, context.UserPrompt, 3, builder.Rounds, ct).ConfigureAwait(false);
         if (ki is not null) r3Ki.Add(ki);
      }

      // Round 3: Defence
      var r2Text = FormatRoundResponses(round2);
      var r2OpText = FormatOperatorInteractions(r2Op);
      var r3BasePrompt = BuildChunkedPrompt(context, 3, totalRounds, builder.Rounds);
      var r3Sys = $"""
                   {baseSystemPrompt}
                   Defend your position. Address every critique, strengthen weak arguments,
                   provide additional evidence, and concede where critics are right.
                   """;
      var r3Prompt = $"""
                      Original question: {r3BasePrompt}
                      Initial responses: {r1Text}
                      Critiques: {r2Text}
                      {(string.IsNullOrWhiteSpace(r1OpText) && string.IsNullOrWhiteSpace(r2OpText) ? "" : $"\n{r1OpText}\n{r2OpText}")}
                      Defend your position: 1) Address each criticism 2) Strengthen weak points 3) Concede where right 4) Final answer
                      """;
      var round3StartedAt = DateTime.UtcNow;
       var r3 = await CollectResponsesAsync(members, r3Sys, r3Prompt, temperature, ct).ConfigureAwait(false);
       var r3Op = await ProcessOperatorRequestsAsync(@operator, r3, executionOptions, ct).ConfigureAwait(false);
      var round3 = CreateRound(3, "Defence & Counter-Arguments", "Models defend their positions.", r3, knowledgeInteractions: r3Ki, operatorInteractions: r3Op, startedAt: round3StartedAt);
      builder.AddRound(round3);
      onRoundCompleted?.Invoke(round3);

       return await FinalizeAsync(builder, chairman, knowledgeKeeper, temperature, onRoundCompleted, ct).ConfigureAwait(false);
   }

   private static async Task<DebateResult> FinalizeAsync(
      DebateResultBuilder builder,
      CouncilMember? chairman,
      KnowledgeKeeper? knowledgeKeeper,
      float temperature,
      Action<DebateRound>? onRoundCompleted,
      CancellationToken ct)
   {
      // Round 4: Judge verdict
      if (chairman is not null)
         try
         {
            var round4StartedAt = DateTime.UtcNow;
            // The Judge's final verdict is the single most important output of the
            // debate. If the caller's budget CT fired mid-round we still want a real
            // verdict (not the "[JUDGE ERROR: …]" placeholder). Switch to
            // CancellationToken.None so an exhausted wall-clock budget doesn't kill
            // the synthesis. If the *caller* genuinely wants to cancel (not just the
            // internal budget), the caller's CT will surface as a thrown
            // OperationCanceledException higher up — the debate itself never throws.
            var verdictCt = ct.IsCancellationRequested
               ? CancellationToken.None
               : ct;
            var verdict = await Chairman.SynthesizeVerdictAsync(chairman, builder.Context, builder.Rounds, knowledgeKeeper, temperature, verdictCt).ConfigureAwait(false);
            builder.SetFinalVerdict(verdict);

            var round4 = CreateRound(4, "Judge's Verdict", "The Chairman judges the debate.",
               new Dictionary<string, string> { [chairman.DisplayName] = verdict },
               startedAt: round4StartedAt);
            builder.AddRound(round4);
            onRoundCompleted?.Invoke(round4);
         }
         catch (OperationCanceledException) when (ct.IsCancellationRequested)
         {
            // Genuine caller cancellation — let it propagate; no point producing a
            // verdict the caller is no longer interested in.
            throw;
         }
         catch (Exception ex)
         {
            builder.SetFinalVerdict($"[JUDGE ERROR: {ex.Message}]");
         }

      builder.MarkCompleted();
      return builder.Build();
   }
}
