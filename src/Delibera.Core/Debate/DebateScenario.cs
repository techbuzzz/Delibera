using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
/// Abstract base class for debate strategies.
/// Provides shared utilities for collecting responses, formatting rounds,
/// querying the Knowledge Keeper, and compressing context.
/// </summary>
public abstract class DebateScenario : IDebateStrategy
{
   /// <inheritdoc/>
   public abstract string StrategyName { get; }

   /// <inheritdoc/>
   public abstract string Description { get; }

   /// <inheritdoc/>
   public abstract Task<DebateResult> ExecuteAsync(
       IReadOnlyList<CouncilMember> members,
       PromptContext context,
       CouncilMember? chairman,
       KnowledgeKeeper? knowledgeKeeper,
       int maxRounds = 4,
       float temperature = 0.7f,
       Action<DebateRound>? onRoundCompleted = null,
       CancellationToken ct = default);

   // ──────────────────────────────────────────────
   // Shared helpers
   // ──────────────────────────────────────────────

   /// <summary>Collects responses from all members in parallel.</summary>
   protected static async Task<Dictionary<string, string>> CollectResponsesAsync(
       IReadOnlyList<CouncilMember> members,
       string systemPrompt,
       string userPrompt,
       float temperature,
       CancellationToken ct)
   {
      var tasks = members.Select(async member =>
      {
         try
         {
            var response = await member.AskAsync(systemPrompt, userPrompt, temperature, ct);
            return (member.DisplayName, Response: response);
         }
         catch (Exception ex)
         {
            return (member.DisplayName, Response: $"[ERROR: {ex.Message}]");
         }
      });

      var results = await Task.WhenAll(tasks);
      return results.ToDictionary(r => r.DisplayName, r => r.Response);
   }

   /// <summary>Formats a single round's responses into readable text.</summary>
   protected static string FormatRoundResponses(DebateRound round)
   {
      var sb = new StringBuilder();
      sb.AppendLine($"=== {round.RoundName} ===");
      foreach (var (member, response) in round.Responses)
      {
         sb.AppendLine($"\n--- {member} ---");
         sb.AppendLine(response);
      }
      return sb.ToString();
   }

   /// <summary>Formats all rounds into a single text block.</summary>
   protected static string FormatAllRounds(IReadOnlyList<DebateRound> rounds) =>
       string.Join("\n\n", rounds.Select(FormatRoundResponses));

   /// <summary>Creates a completed debate round.</summary>
   protected static DebateRound CreateRound(
       int number,
       string name,
       string? description,
       Dictionary<string, string> responses,
       string? prompt = null,
       IReadOnlyList<KnowledgeInteraction>? knowledgeInteractions = null) =>
       new()
       {
          RoundNumber = number,
          RoundName = name,
          Description = description,
          Responses = responses,
          RoundPrompt = prompt,
          KnowledgeInteractions = knowledgeInteractions ?? [],
          CompletedAt = DateTime.UtcNow
       };

   /// <summary>
   /// Optionally queries the Knowledge Keeper for context relevant to the debate topic.
   /// Returns the answer text or empty string if no keeper is available.
   /// </summary>
   protected static async Task<(string context, KnowledgeInteraction? interaction)> QueryKnowledgeAsync(
       KnowledgeKeeper? keeper,
       string query,
       float temperature = 0.3f,
       CancellationToken ct = default)
   {
      if (keeper is null) return (string.Empty, null);

      try
      {
         var answer = await keeper.AnswerQuestionAsync(query, 5, temperature, ct);
         var interaction = new KnowledgeInteraction(query, answer, 5);
         return (answer, interaction);
      }
      catch
      {
         return (string.Empty, null);
      }
   }

   /// <summary>
   /// Queries the Knowledge Keeper with structured per-round context.
   /// Enhanced version that provides round-aware context with sources.
   /// </summary>
   /// <param name="keeper">Knowledge Keeper instance.</param>
   /// <param name="topic">Debate topic.</param>
   /// <param name="roundNumber">Current round number.</param>
   /// <param name="previousRounds">Previous rounds for context.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Formatted knowledge context and the interaction record.</returns>
   protected static async Task<(string context, KnowledgeInteraction? interaction)> QueryKnowledgeForRoundAsync(
       KnowledgeKeeper? keeper,
       string topic,
       int roundNumber,
       IReadOnlyList<DebateRound>? previousRounds = null,
       CancellationToken ct = default)
   {
      if (keeper is null) return (string.Empty, null);

      try
      {
         var previousSummary = previousRounds is { Count: > 0 }
             ? string.Join("\n", previousRounds.Select(r =>
                 $"Round {r.RoundNumber}: " + string.Join("; ",
                     r.Responses.Select(kv => $"{kv.Key}: {kv.Value[..Math.Min(200, kv.Value.Length)]}"))))
             : null;

         var roundCtx = await keeper.ProvideContextForRoundAsync(
             topic, roundNumber, previousSummary, ct: ct);

         return (roundCtx.Answer, roundCtx.Interaction);
      }
      catch
      {
         return (string.Empty, null);
      }
   }
}
