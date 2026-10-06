using System.Text;
using System.Text.RegularExpressions;
using Delibera.Core.Compression;
using Delibera.Core.Council;

namespace Delibera.Core.Debate;

/// <summary>
///    Abstract base class for debate strategies.
///    Provides shared utilities for collecting responses, formatting rounds,
///    querying the Knowledge Keeper, and compressing context.
/// </summary>
public abstract partial class DebateScenario : IDebateStrategy
{
   // ──────────────────────────────────────────────
   // Operator helpers
   // ──────────────────────────────────────────────

   /// <summary>
   ///    Matches the marker participants use to delegate a task to the Operator,
   ///    e.g.: <c>[[OPERATOR: search the web for the latest .NET 10 release notes]]</c>.
   ///    <para>
   ///    Source-generated rather than <c>RegexOptions.Compiled</c>: the pattern is a
   ///    compile-time literal, so the generator emits it at build time — no runtime JIT,
   ///    no static-initialisation cost, and it keeps the pattern AOT/trim-safe.
   ///    </para>
   /// </summary>
   [GeneratedRegex(@"\[\[\s*OPERATOR\s*:\s*(?<task>.+?)\]\]", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
   private static partial Regex OperatorRequestRegex();

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
      DebateExecutionOptions executionOptions,
      int maxRounds = 4,
      float temperature = 0.7f,
      Func<DebateRound, CancellationToken, ValueTask>? onRoundCompleted = null,
      CancellationToken ct = default);

   // ──────────────────────────────────────────────
   // Shared helpers
   // ──────────────────────────────────────────────

   /// <summary>
   ///    Collects responses from all members in parallel.
   ///    <para>
   ///    When <paramref name="executionOptions" /> carries a positive
   ///    <c>MaxDegreeOfParallelism</c> it also bounds the fan-out; the operator and
   ///    knowledge-keeper paths already honoured it, this one did not.
   ///    </para>
   /// </summary>
   protected static async Task<Dictionary<string, string>> CollectResponsesAsync(
      IReadOnlyList<CouncilMember> members,
      string systemPrompt,
      string userPrompt,
      float temperature,
      CancellationToken ct,
      DebateExecutionOptions? executionOptions = null,
      List<MemberFailure>? failures = null,
      int roundNumber = 0,
      string roundName = "")
   {
      // Bounding the fan-out keeps a large council from exhausting the HttpClient socket
      // pool or tripping provider rate limits. A semaphore is used rather than
      // Parallel.ForEachAsync because the results must keep member order: the disambiguation
      // below appends "#2", "#3" in the order results arrive.
      var maxParallelism = executionOptions?.MaxDegreeOfParallelism ?? 0;
      var gates = maxParallelism > 0 ? new SemaphoreSlim(maxParallelism, maxParallelism) : null;

      // Context compression. Historically the compressor was configured on the council but
      // never invoked: CompressTextAsync is public API that nothing in the pipeline called, so
      // every debate paid the full, growing transcript cost and DebateResult.TokenStats stayed
      // null. The prompt that is about to be sent to every member is the thing compression
      // exists to shrink, and in later rounds the accumulated transcript dominates it.
      var effectivePrompt = await CompressPromptAsync(
         userPrompt, roundNumber, roundName, executionOptions, ct).ConfigureAwait(false);

      var tasks = members.Select(async member =>
      {
         if (gates is not null) await gates.WaitAsync(ct).ConfigureAwait(false);
         try
         {
            var response = await member.AskAsync(systemPrompt, effectivePrompt, temperature, ct).ConfigureAwait(false);
            return (member.Role, member.DisplayName, Response: response, Failed: false, Error: (string?)null);
         }
         catch (Exception ex)
         {
            return (member.Role, member.DisplayName, Response: (string?)null, Failed: true, Error: (string?)ex.Message);
         }
         finally
         {
            gates?.Release();
         }
      });

      var results = await Task.WhenAll(tasks).ConfigureAwait(false);
      gates?.Dispose();
      //return results.ToDictionary(r => r.DisplayName, r => r.Response);
      // Disambiguate by appending a counter while preserving the original label for unique names.
      var seen = new HashSet<string>();
      var responses = new Dictionary<string, string>(results.Length);
      foreach (var (role, displayName, response, failed, error) in results)
      {
         var key = $"{role}: {displayName}";
         if (!seen.Add(key))
         {
            var index = 2;
            while (!seen.Add($"{key} #{index}"))
               index++;

            key = $"{key} #{index}";
         }

         if (failed)
         {
            // A failed member is omitted from the round entirely. Substituting an "[ERROR: ...]"
            // string here used to put an error into the transcript where the Chairman read it as
            // an opinion, producing a verdict that looked complete but was not.
            failures?.Add(new MemberFailure(
               roundNumber, roundName, role, displayName, ResolveModel(members, displayName),
               error ?? "unknown error"));
            continue;
         }

         if (response is not null)
            responses[key] = response;
      }

      return responses;
   }

   /// <summary>Resolves a member's model name for failure reporting, tolerating duplicate display names.</summary>
   private static string ResolveModel(IReadOnlyList<CouncilMember> members, string displayName)
   {
      foreach (var m in members)
         if (string.Equals(m.DisplayName, displayName, StringComparison.Ordinal))
            return m.ModelName;

      return "unknown";
   }

   /// <summary>
   ///   Compresses a round prompt when the configured compressor would actually save something.
   /// </summary>
   /// <remarks>
   ///   Compression is best-effort: a compressor that throws or returns nothing leaves the prompt
   ///   untouched, because a debate is more valuable than a few saved tokens. Each attempt appends a
   ///   <see cref="CompressionLog" /> so the saving is observable afterwards - which is how the
   ///   long-standing "compression silently does nothing" defect became visible in the first place.
   /// </remarks>
   protected static async Task<string> CompressPromptAsync(
      string prompt,
      int roundNumber,
      string roundName,
      DebateExecutionOptions? executionOptions,
      CancellationToken ct)
   {
      ArgumentNullException.ThrowIfNull(prompt);

      var compressor = executionOptions?.ContextCompressor;
      var logs = executionOptions?.CompressionLogs;
      if (compressor is null)
         return prompt;

      var counter = TokenCounter.Default;
      var originalTokens = counter.EstimateTokens(prompt);
      if (originalTokens < executionOptions!.CompressionThresholdTokens)
         return prompt;

      var sw = System.Diagnostics.Stopwatch.StartNew();
      try
      {
         var result = await compressor
            .CompressAsync(prompt, executionOptions.ContextCompressionOptions, ct)
            .ConfigureAwait(false);
         sw.Stop();

         // A compressor that returns more text than it started with is not helping; keep the original.
         if (string.IsNullOrWhiteSpace(result.Text) || result.OriginalTokens <= result.CompressedTokens)
         {
            logs?.Add(new CompressionLog
            {
               RoundNumber = roundNumber,
               Description = $"Round {roundNumber} prompt ({roundName})",
               StrategyName = result.StrategyUsed ?? compressor.StrategyName,
               OriginalTokens = result.OriginalTokens,
               CompressedTokens = result.OriginalTokens,
               Duration = sw.Elapsed
            });
            return prompt;
         }

         logs?.Add(new CompressionLog
         {
            RoundNumber = roundNumber,
            Description = $"Round {roundNumber} prompt ({roundName})",
            StrategyName = result.StrategyUsed ?? compressor.StrategyName,
            OriginalTokens = result.OriginalTokens,
            CompressedTokens = result.CompressedTokens,
            Duration = sw.Elapsed
         });

         return result.Text;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
         sw.Stop();
         logs?.Add(new CompressionLog
         {
            RoundNumber = roundNumber,
            Description = $"Round {roundNumber} prompt ({roundName})",
            StrategyName = compressor.StrategyName,
            OriginalTokens = originalTokens,
            CompressedTokens = originalTokens,
            Duration = sw.Elapsed
         });
         return prompt;
      }
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
         var answer = await keeper.AnswerQuestionAsync(query, 5, temperature, ct).ConfigureAwait(false);
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
         // One buffer for the whole summary. The nested Join/Select/interpolation form allocated
         // an interpolated string plus a joined string per response, then joined those again —
         // six or more allocations per response, on a per-round path.
         string? previousSummary = null;
         if (previousRounds is { Count: > 0 })
         {
            var summary = new StringBuilder();
            foreach (var r in previousRounds)
            {
               if (summary.Length > 0)
                  summary.Append('\n');

               summary.Append("Round ").Append(r.RoundNumber).Append(": ");
               var firstResponse = true;
               foreach (var kv in r.Responses)
               {
                  if (!firstResponse)
                     summary.Append("; ");

                  firstResponse = false;
                  summary.Append(kv.Key).Append(": ");
                  summary.Append(kv.Value.AsSpan(0, Math.Min(200, kv.Value.Length)));
               }
            }

            previousSummary = summary.ToString();
         }

         var roundCtx = await keeper.ProvideContextForRoundAsync(
            topic, roundNumber, previousSummary, ct: ct).ConfigureAwait(false);

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
      return await ProcessOperatorRequestsAsync(@operator, responses, DebateExecutionOptions.Default, ct).ConfigureAwait(false);
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

         // The marker is a literal "[["; without it the regex engine has nothing to
         // anchor on, and a lazy `(.+?)` with Singleline walks the whole response. The
         // cheapest possible pre-check skips that for every response that never delegates.
         if (!response.Contains("[[", StringComparison.Ordinal)) continue;

         foreach (Match match in OperatorRequestRegex().Matches(response))
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
               var result = await @operator.ExecuteTaskAsync(item.Member, item.Task, token).ConfigureAwait(false);
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
   ///    Builds the user prompt for a specific round, respecting AutoChunking when enabled.
   ///    When the context has a <see cref="PromptContext.ChunkingPlan" /> and
   ///    <see cref="PromptContext.AutoChunkingEnabled" /> is <c>true</c>, returns the
   ///    chunk-appropriate prompt. Otherwise falls back to <see cref="PromptContext.GetFullUserPrompt" />.
   /// </summary>
   /// <param name="context">The prompt context.</param>
   /// <param name="roundNumber">Current round number (1-based).</param>
   /// <param name="totalRounds">Total number of rounds in the debate.</param>
   /// <param name="previousRounds">Previous rounds for context continuity (optional).</param>
   /// <returns>The formatted user prompt for this round.</returns>
   protected static string BuildChunkedPrompt(
      PromptContext context,
      int roundNumber,
      int totalRounds,
      IReadOnlyList<DebateRound>? previousRounds = null)
   {
      if (context.AutoChunkingEnabled && context.ChunkingPlan is not null)
         return context.GetChunkedUserPrompt(roundNumber, totalRounds, previousRounds);

      return context.GetFullUserPrompt();
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
