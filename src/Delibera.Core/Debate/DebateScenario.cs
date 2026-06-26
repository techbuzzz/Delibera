using System.Text.RegularExpressions;
using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
///    Abstract base class for debate strategies.
///    Provides shared utilities for collecting responses, formatting rounds,
///    querying the Knowledge Keeper, and compressing context.
/// </summary>
public abstract class DebateScenario : IDebateStrategyWithOptions
{
   // ──────────────────────────────────────────────
   // Operator helpers
   // ──────────────────────────────────────────────

   /// <summary>
   ///    Marker participants use to delegate a task to the Operator, e.g.:
   ///    <c>[[OPERATOR: search the web for the latest .NET 10 release notes]]</c>.
   /// </summary>
   private static readonly Regex OperatorRequestRegex =
      new(@"\[\[\s*OPERATOR\s*:\s*(?<task>.+?)\]\]",
         RegexOptions.Singleline |
         RegexOptions.IgnoreCase |
         RegexOptions.Compiled);

   /// <inheritdoc />
   public abstract string StrategyName { get; }

   /// <inheritdoc />
   public abstract string Description { get; }

   /// <inheritdoc />
   public abstract Task<DebateResult> ExecuteAsync(
      IReadOnlyList<CouncilMember> members,
      PromptContext context,
      CouncilMember? chairman,
      KnowledgeKeeper? knowledgeKeeper,
      Operator? @operator,
      int maxRounds = 4,
      float temperature = 0.7f,
      Action<DebateRound>? onRoundCompleted = null,
      CancellationToken ct = default);

    /// <inheritdoc cref="IDebateStrategyWithOptions.ExecuteAsync(IReadOnlyList{CouncilMember}, PromptContext, CouncilMember?, KnowledgeKeeper?, Operator?, DebateExecutionOptions, int, float, Action{DebateRound}?, CancellationToken)" />
    public abstract Task<DebateResult> ExecuteAsync(
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
            return (member.Role, member.DisplayName, Response: response);
         }
         catch (Exception ex)
         {
            return (member.Role, member.DisplayName, Response: $"[ERROR: {ex.Message}]");
         }
      });

      var results = await Task.WhenAll(tasks);
      //return results.ToDictionary(r => r.DisplayName, r => r.Response);
      // Disambiguate by appending a counter while preserving the original label for unique names.
      var seen = new HashSet<string>();
      var responses = new Dictionary<string, string>(results.Length);
      foreach (var (role, displayName, response) in results)
      {
         var key = $"{role}: {displayName}";
         if (!seen.Add(key))
         {
            var index = 2;
            while (!seen.Add($"{key} #{index}"))
               index++;

            key = $"{key} #{index}";
         }

         responses[key] = response;
      }

      return responses;
   }

   /// <summary>Formats a single round's responses into readable text.</summary>
   protected static string FormatRoundResponses(DebateRound round)
   {
      ArgumentNullException.ThrowIfNull(round);
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
   protected static string FormatAllRounds(IReadOnlyList<DebateRound> rounds)
   {
      return string.Join("\n\n", rounds.Select(FormatRoundResponses));
   }

   /// <summary>Creates a completed debate round with an explicit start time.</summary>
   protected static DebateRound CreateRound(
      int number,
      string name,
      string? description,
      Dictionary<string, string> responses,
      string? prompt = null,
      IReadOnlyList<KnowledgeInteraction>? knowledgeInteractions = null,
      IReadOnlyList<OperatorInteraction>? operatorInteractions = null,
      DateTime? startedAt = null)
   {
      return new DebateRound
      {
         RoundNumber = number,
         RoundName = name,
         Description = description,
         Responses = responses,
         RoundPrompt = prompt,
         KnowledgeInteractions = knowledgeInteractions ?? [],
         OperatorInteractions = operatorInteractions ?? [],
         StartedAt = startedAt ?? DateTime.UtcNow,
         CompletedAt = DateTime.UtcNow
      };
   }

   /// <summary>
   ///    Optionally queries the Knowledge Keeper for context relevant to the debate topic.
   ///    Returns the answer text or empty string if no keeper is available.
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
   ///    Queries the Knowledge Keeper with structured per-round context.
   ///    Enhanced version that provides round-aware context with sources.
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
               $"Round {r.RoundNumber}: " +
               string.Join("; ",
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

   /// <summary>
   ///    Builds an "Operator briefing" describing the Operator's tools and how to delegate
   ///    tasks to it. Appended to the participants' system prompt so they know what is available.
   ///    Returns an empty string when no Operator is configured.
   /// </summary>
   protected static string BuildOperatorBriefing(Operator? @operator)
   {
      if (@operator is null) return string.Empty;

      return $"""

              ── OPERATOR (tools available) ──
              A shared Operator agent is available to all participants.
              {@operator.GetToolCatalog()}

              To delegate a task to the Operator, include a line in your response using this exact marker:
              [[OPERATOR: <your natural-language task here>]]
              For example: [[OPERATOR: search the web for recent benchmarks comparing PostgreSQL and MySQL]]
              The Operator's answer will be provided to all participants in the next round.
              Only delegate when external information or actions (web search, database lookup, writing to Notion, etc.) are genuinely needed.
              """;
   }

   /// <summary>
   ///    Scans participant responses for Operator request markers, executes each delegated
   ///    task via the Operator, and returns the recorded interactions.
   /// </summary>
   /// <param name="operator">Operator instance (may be <c>null</c>).</param>
   /// <param name="responses">Participant responses keyed by display name.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Operator interactions produced during this round.</returns>
   protected static async Task<IReadOnlyList<OperatorInteraction>> ProcessOperatorRequestsAsync(
      Operator? @operator,
      IReadOnlyDictionary<string, string> responses,
      CancellationToken ct = default)
   {
      return await ProcessOperatorRequestsAsync(@operator, responses, DebateExecutionOptions.Default, ct);
   }

   /// <summary>
   ///    Scans participant responses for Operator request markers, executes each delegated
   ///    task via the Operator (in parallel, bounded by
   ///    <see cref="DebateExecutionOptions.MaxDegreeOfParallelism" />), and returns the
   ///    recorded interactions.
   /// </summary>
   protected static async Task<IReadOnlyList<OperatorInteraction>> ProcessOperatorRequestsAsync(
      Operator? @operator,
      IReadOnlyDictionary<string, string> responses,
      DebateExecutionOptions executionOptions,
      CancellationToken ct = default)
   {
      if (@operator is null || responses.Count == 0) return [];

      // Collect every (member, task) pair across all responses first.
      var pending = new List<(string Member, string Task)>();
      foreach (var (member, response) in responses)
      {
         if (string.IsNullOrWhiteSpace(response)) continue;

         foreach (Match match in OperatorRequestRegex.Matches(response))
         {
            var task = match.Groups["task"].Value.Trim();
            if (string.IsNullOrWhiteSpace(task)) continue;
            pending.Add((member, task));
         }
      }

      if (pending.Count == 0) return [];

      var interactions = new List<OperatorInteraction>(pending.Count);
      var parallelOpts = executionOptions.ToParallelOptions(ct);

      // Parallel.ForEachAsync gives us a concurrent, optionally-bounded execution of the
      // delegated Operator tasks. Results are collected in a thread-safe list.
      await Parallel.ForEachAsync(
         pending,
         parallelOpts,
         async (item, token) =>
         {
            try
            {
               var result = await @operator.ExecuteTaskAsync(item.Member, item.Task, token);
               var interaction = result.ToInteraction();
               lock (interactions)
               {
                  interactions.Add(interaction);
               }
            }
            catch (Exception ex)
            {
               lock (interactions)
               {
                  interactions.Add(new OperatorInteraction(
                     item.Member, item.Task, $"[Operator error: {ex.Message}]", [], false, DateTime.UtcNow));
               }
            }
         });

      return interactions;
   }

   /// <summary>
   ///    Formats Operator interactions into a context block that can be injected into the
   ///    next round's prompt so participants can use the Operator's findings.
   /// </summary>
   protected static string FormatOperatorInteractions(IReadOnlyList<OperatorInteraction> interactions)
   {
      if (interactions is not { Count: > 0 }) return string.Empty;

      var sb = new StringBuilder();
      sb.AppendLine("🛠️ Operator results (requested by participants):");
      foreach (var i in interactions)
      {
         var tools = i.ToolCalls.Count > 0
            ? string.Join(", ", i.ToolCalls.Select(c => $"{c.ServerName}.{c.ToolName}"))
            : "no tools";
         sb.AppendLine($"\n• {i.RequesterName} asked: {i.Task}");
         sb.AppendLine($"  Tools used: {tools}");
         sb.AppendLine($"  Answer: {i.Answer}");
      }

      return sb.ToString();
   }
}