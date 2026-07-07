using Delibera.Core.Voting;

namespace Delibera.Core.Council;

/// <summary>
///    Factory and helper for the Chairman role — the lead moderator of the council.
///    The Chairman opens the debate, explains the strategy, moderates each round,
///    asks clarifying questions, enforces regulations, and synthesises the final verdict.
/// </summary>
public static class Chairman
{
   /// <summary>
   ///    Marker prefix that identifies a voting Chairman's persona prompt.
   ///    <see cref="CouncilExecutor" /> checks for this to decide whether to run the
   ///    voting engine instead of the standard synthesis.
   /// </summary>
   public const string VotingChairmanMarker = "##VOTING##";
// ──────────────────────────────────────────────
// Factory methods
// ──────────────────────────────────────────────

   /// <summary>Creates a standard, neutral Chairman.</summary>
   public static CouncilMember CreateStandard(string modelName, ILLMProvider provider)
   {
      return new CouncilMember(modelName, provider, "Chairman",
         """
         You are the Chairman of an AI Council debate — neutral, objective and fair.
         You organise the debate process, ensure every participant is heard,
         synthesise diverse viewpoints and produce clear, actionable verdicts.
         Prioritise accuracy, completeness and practical usefulness.
         """);
   }

   /// <summary>Creates a strict, evidence-focused Chairman.</summary>
   public static CouncilMember CreateStrict(string modelName, ILLMProvider provider)
   {
      return new CouncilMember(modelName, provider, "Strict Chairman",
         """
         You are a strict, evidence-focused Chairman. You demand factual accuracy,
         reject unsupported claims and penalise logical fallacies.
         If no participant provides a satisfactory answer, say so clearly.
         """);
   }

   /// <summary>Creates a creative Chairman who encourages novel thinking.</summary>
   public static CouncilMember CreateCreative(string modelName, ILLMProvider provider)
   {
      return new CouncilMember(modelName, provider, "Creative Chairman",
         """
         You are a creative Chairman who looks beyond conventional answers.
         While maintaining accuracy, you seek innovative connections between ideas,
         encourage thinking outside the box, and surface unexpected insights.
         """);
   }

   /// <summary>Creates a Chairman with a custom persona prompt.</summary>
   public static CouncilMember CreateCustom(string modelName, ILLMProvider provider, string personaPrompt)
   {
      return new CouncilMember(modelName, provider, "Chairman", personaPrompt);
   }

   /// <summary>
   ///    Creates a voting Chairman that uses an <see cref="IVotingStrategy" /> to reach
   ///    a decision via structured voting among participants, as an alternative to the
   ///    single-LLM Chairman synthesis. The strategy instance is attached to the
   ///    member's <see cref="CouncilMember.PersonaPrompt" /> so the executor can detect
   ///    it and route the final round through the voting engine.
   /// </summary>
   /// <param name="modelName">Chairman model name.</param>
   /// <param name="provider">LLM provider.</param>
   /// <param name="votingStrategy">Voting strategy (Majority, BordaCount, Weighted, ...).</param>
   /// <returns>A Chairman member whose <see cref="CouncilMember.PersonaPrompt" /> encodes the voting strategy.</returns>
   /// <remarks>
   ///    The returned member is wired into <see cref="CouncilBuilder.SetChairman(CouncilMember)" />
   ///    as usual. <see cref="CouncilExecutor" /> detects the encoded strategy and, after
   ///    the final round, asks each participant to rank the options, builds
   ///    <see cref="ParticipantBallot" />s, and calls <see cref="IVotingStrategy.TallyAsync" />.
   ///    The <see cref="VotingResult" /> is attached to <see cref="Models.DebateResult.VotingTally" />
   ///    and rendered as a 🗳️ Voting Tally section in the Markdown output.
   /// </remarks>
   public static CouncilMember CreateVoting(string modelName, ILLMProvider provider, IVotingStrategy votingStrategy)
   {
      ArgumentNullException.ThrowIfNull(votingStrategy);
      // Encode the strategy as a special marker in the persona prompt so the executor
      // can detect it without changing the CouncilMember type. The marker is a
      // well-known prefix that CouncilExecutor checks for.
      var persona = $"{VotingChairmanMarker}{votingStrategy.MethodName}:{votingStrategy.GetType().FullName}";
      return new CouncilMember(modelName, provider, "Voting Chairman", persona);
   }

   // ──────────────────────────────────────────────
   // Specialised Chairman actions (each has its own prompt)
   // ──────────────────────────────────────────────

   /// <summary>
   ///    Opens the debate: greets participants, states the topic, and outlines rules.
   /// </summary>
   public static Task<string> OpenDebateAsync(
      CouncilMember chairman,
      PromptContext context,
      IReadOnlyList<CouncilMember> members,
      string strategyName,
      int maxRounds,
      float temperature = 0.5f,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(members);
      var participants = string.Join(", ", members.Select(m => m.DisplayName));

      var prompt = $"""
                    You are the Chairman opening a council debate.

                    **Topic / Question:** {context.UserPrompt}
                    **Participants:** {participants}
                    **Strategy:** {strategyName}
                    **Max rounds:** {maxRounds}

                    Please:
                    1. Welcome the participants
                    2. Clearly state the topic under discussion
                    3. Briefly describe the debate strategy and how many rounds there will be
                    4. Remind participants of the rules (be factual, cite evidence, stay on topic)
                    5. Open the floor for Round 1

                    Keep it concise — 4-6 sentences.
                    """;

      return chairman.AskAsync(context.SystemPrompt, prompt, temperature, ct);
   }

   /// <summary>
   ///    Explains the chosen debate strategy to participants.
   /// </summary>
   public static Task<string> ExplainStrategyAsync(
      CouncilMember chairman,
      string strategyName,
      string strategyDescription,
      float temperature = 0.5f,
      CancellationToken ct = default)
   {
      var prompt = $"""
                    As the Chairman, explain the debate strategy to participants.

                    **Strategy:** {strategyName}
                    **Description:** {strategyDescription}

                    Explain:
                    1. How this strategy works
                    2. What is expected from participants in each round
                    3. How the final verdict will be determined

                    Be clear and concise — 3-5 sentences.
                    """;

      return chairman.AskAsync("You are the Chairman of an AI Council.", prompt, temperature, ct);
   }

   /// <summary>
   ///    Moderates a discussion by reviewing the latest round's responses
   ///    and providing guidance for the next round.
   /// </summary>
   public static Task<string> ModerateDiscussionAsync(
      CouncilMember chairman,
      PromptContext context,
      DebateRound latestRound,
      float temperature = 0.5f,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(latestRound);
      var responses = string.Join("\n\n", latestRound.Responses.Select(r => $"**{r.Key}:** {r.Value}"));

      var prompt = $"""
                    As the Chairman, review the latest round and moderate the discussion.

                    **Round {latestRound.RoundNumber}: {latestRound.RoundName}**

                    Responses:
                    {responses}

                    Please:
                    1. Summarise the key points raised
                    2. Identify areas of agreement and disagreement
                    3. Point out any weak arguments or unsupported claims
                    4. Provide guidance for the next round

                    Be concise and constructive — 4-8 sentences.
                    """;

      return chairman.AskAsync(context.SystemPrompt, prompt, temperature, ct);
   }

   /// <summary>
   ///    Generates clarifying questions for participants based on the debate so far.
   /// </summary>
   public static Task<string> AskClarifyingQuestionsAsync(
      CouncilMember chairman,
      PromptContext context,
      IReadOnlyList<DebateRound> rounds,
      float temperature = 0.6f,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(rounds);
      var roundsSummary = string.Join("\n\n",
         rounds.Select(r => $"**Round {r.RoundNumber} ({r.RoundName}):**\n" +
                            string.Join("\n", r.Responses.Select(kv => $"- {kv.Key}: {kv.Value[..Math.Min(200, kv.Value.Length)]}..."))));

      var prompt = $"""
                    As the Chairman, review the debate so far and formulate 2-3 targeted
                    clarifying questions that will help resolve remaining ambiguities.

                    **Topic:** {context.UserPrompt}

                    **Debate so far:**
                    {roundsSummary}

                    For each question:
                    1. State which participant(s) it is directed at
                    2. Explain why this clarification is needed
                    3. Phrase the question clearly

                    Be concise.
                    """;

      return chairman.AskAsync(context.SystemPrompt, prompt, temperature, ct);
   }

   /// <summary>
   ///    Enforces regulations — checks that participants are following debate rules.
   /// </summary>
   public static Task<string> EnforceRegulationsAsync(
      CouncilMember chairman,
      DebateRound latestRound,
      float temperature = 0.3f,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(latestRound);
      var responses = string.Join("\n\n", latestRound.Responses.Select(r => $"**{r.Key}:** {r.Value}"));

      var prompt = $"""
                    As the Chairman, check whether all participants are following the debate rules.

                    Rules to check:
                    1. Responses must be on-topic
                    2. Claims must be supported by reasoning or evidence
                    3. Responses should not contain personal attacks
                    4. Participants must directly address the question
                    5. Responses must be substantive (not empty or evasive)

                    **Round {latestRound.RoundNumber}: {latestRound.RoundName}**

                    {responses}

                    For any violations, state which participant violated which rule and why.
                    If all participants followed the rules, simply state "All participants are in compliance."
                    """;

      return chairman.AskAsync("You are a strict debate regulator.", prompt, temperature, ct);
   }

   /// <summary>
   ///    Synthesises the final verdict after all rounds are complete.
   /// </summary>
   public static async Task<string> SynthesizeVerdictAsync(
      CouncilMember chairman,
      PromptContext context,
      IReadOnlyList<DebateRound> rounds,
      KnowledgeKeeper? knowledgeKeeper = null,
      float temperature = 0.5f,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(rounds);
      var allRounds = string.Join("\n\n",
         rounds.Select(r =>
         {
            var header = $"### Round {r.RoundNumber}: {r.RoundName}";
            var body = string.Join("\n", r.Responses.Select(kv => $"**{kv.Key}:**\n{kv.Value}"));
            var ki = r.KnowledgeInteractions is { Count: > 0 }
               ? "\n📚 Knowledge queries:\n" + string.Join("\n", r.KnowledgeInteractions.Select(k => $"Q: {k.Query}\nA: {k.Answer}"))
               : "";
            return $"{header}\n{body}{ki}";
         }));

      // Optionally ask the Knowledge Keeper for a final fact-check
      var knowledgeNote = "";
      if (knowledgeKeeper is not null)
         try
         {
            knowledgeNote = await knowledgeKeeper.AnswerQuestionAsync(
               $"Provide key facts relevant to: {context.UserPrompt}", 3, 0.3f, ct);
            knowledgeNote = $"\n\n📚 Knowledge Keeper's fact summary:\n{knowledgeNote}";
         }
         catch
         {
            /* If RAG fails, proceed without it */
         }

      var prompt = $"""
                    As the Chairman, synthesise the FINAL VERDICT for this council debate.

                    **Original Question:** {context.GetFullUserPrompt()}

                    **Complete Debate History:**
                    {allRounds}
                    {knowledgeNote}

                    Your verdict MUST include:
                    1. **Summary** — key points from the debate
                    2. **Consensus** — areas where participants agreed
                    3. **Dissent** — areas of remaining disagreement
                    4. **Final Answer** — your definitive, synthesised answer
                    5. **Confidence** — 1-10 score for the quality of this verdict
                    6. **Recommendations** — actionable next steps (if applicable)
                    """;

      return await chairman.AskAsync(context.SystemPrompt, prompt, temperature, ct);
   }
}


