using System.Diagnostics;

namespace Delibera.Core.Telemetry;

/// <summary>
///    High-level facade over <see cref="DeliberaActivitySource" /> and
///    <see cref="DeliberaMeter" />. Provides one-call instrumentation helpers used by
///    the council executor and debate strategies so call sites stay
///    terse and consistent.
/// </summary>
/// <remarks>
///    <para>
///       All methods are no-ops (return <c>null</c> or simply return) when no listener
///       is attached — the standard .NET zero-overhead instrumentation pattern.
///       Callers do not need to check <see cref="TelemetryOptions.Enabled" /> themselves.
///    </para>
///    <para>
///       When <see cref="TelemetryOptions.Enabled" /> is <c>false</c>, the
///       <see cref="Council.CouncilExecutor" /> simply does not call these helpers; this provides
///       an extra layer of guarding for hot paths that would otherwise allocate tag
///       arrays or compute string tags on every round.
///    </para>
/// </remarks>
public static class DeliberaTelemetry
{
   /// <summary>
   ///    Starts the top-level <c>delibera.council.execute</c> span for a debate.
   /// </summary>
   /// <param name="debateId">Debate identifier.</param>
   /// <param name="strategyName">Strategy name.</param>
   /// <param name="memberCount">Number of council participants.</param>
   /// <param name="maxRounds">Configured max rounds.</param>
   /// <returns>The started activity (dispose it at debate end), or <c>null</c>.</returns>
   public static Activity? StartDebate(
      string debateId,
      string strategyName,
      int memberCount,
      int maxRounds)
   {
      return DeliberaActivitySource.StartActivity(
         DeliberaActivityNames.CouncilExecute,
         [
            new KeyValuePair<string, object?>(DeliberaTelemetryTags.DebateId, debateId),
            new KeyValuePair<string, object?>(DeliberaTelemetryTags.StrategyName, strategyName),
            new KeyValuePair<string, object?>("debate.member_count", memberCount),
            new KeyValuePair<string, object?>("debate.max_rounds", maxRounds)
         ]);
   }

   /// <summary>
   ///    Starts a <c>delibera.council.round</c> span for a single debate round.
   /// </summary>
   /// <param name="roundNumber">1-based round number.</param>
   /// <param name="roundName">Round display name.</param>
   /// <returns>The started activity (dispose at round end), or <c>null</c>.</returns>
   public static Activity? StartRound(int roundNumber, string roundName)
   {
      return DeliberaActivitySource.StartActivity(
         DeliberaActivityNames.CouncilRound,
         [
            new KeyValuePair<string, object?>(DeliberaTelemetryTags.RoundNumber, roundNumber),
            new KeyValuePair<string, object?>(DeliberaTelemetryTags.RoundName, roundName)
         ]);
   }

   /// <summary>
   ///    Starts a <c>delibera.member.respond</c> span for a single participant LLM call.
   /// </summary>
   /// <param name="memberName">Participant display name.</param>
   /// <param name="memberRole">Participant role label.</param>
   public static Activity? StartMemberRespond(string memberName, string? memberRole)
   {
      var tags = new List<KeyValuePair<string, object?>>(2)
      {
         new(DeliberaTelemetryTags.MemberName, memberName)
      };
      if (!string.IsNullOrEmpty(memberRole))
         tags.Add(new KeyValuePair<string, object?>(DeliberaTelemetryTags.MemberRole, memberRole));

      return DeliberaActivitySource.StartActivity(DeliberaActivityNames.MemberRespond, tags);
   }

   /// <summary>
   ///    Starts a <c>delibera.rag.query</c> span for a Knowledge Keeper query.
   /// </summary>
   /// <param name="query">Query text (will be truncated to 200 chars in the tag).</param>
   public static Activity? StartRagQuery(string query)
   {
      var truncated = query.Length > 200
         ? query[..200]
         : query;
      return DeliberaActivitySource.StartActivity(
         DeliberaActivityNames.RagQuery,
         DeliberaTelemetryTags.RagQuery,
         truncated);
   }

   /// <summary>
   ///    Starts a <c>delibera.compression</c> span for a context-compression operation.
   /// </summary>
   /// <param name="strategyName">Compression strategy name.</param>
   public static Activity? StartCompression(string strategyName)
   {
      return DeliberaActivitySource.StartActivity(
         DeliberaActivityNames.Compression,
         DeliberaTelemetryTags.CompressionStrategy,
         strategyName);
   }

   /// <summary>
   ///    Starts a <c>delibera.operator.execute</c> span for a single Operator task.
   /// </summary>
   /// <param name="requesterName">Display name of the participant that requested the task.</param>
   /// <param name="task">Task text (truncated for the tag).</param>
   public static Activity? StartOperatorTask(string requesterName, string task)
   {
      var truncated = task.Length > 200
         ? task[..200]
         : task;
      return DeliberaActivitySource.StartActivity(
         DeliberaActivityNames.OperatorExecute,
         [
            new KeyValuePair<string, object?>(DeliberaTelemetryTags.RequesterName, requesterName),
            new KeyValuePair<string, object?>("operator.task", truncated)
         ]);
   }

   /// <summary>
   ///    Starts a <c>delibera.chairman.synthesize</c> span for the Chairman's final-verdict call.
   /// </summary>
   public static Activity? StartChairmanSynthesize()
   {
      return DeliberaActivitySource.StartActivity(DeliberaActivityNames.ChairmanSynthesize);
   }

   /// <summary>
   ///    Starts a <c>delibera.chairman.open</c> span for the Chairman's opening statement.
   /// </summary>
   public static Activity? StartChairmanOpen()
   {
      return DeliberaActivitySource.StartActivity(DeliberaActivityNames.ChairmanOpen);
   }

   // ──────────────────────────────────────────────
   // Metric recording helpers
   // ──────────────────────────────────────────────

   /// <summary>
   ///    Records the total duration of a debate in the <see cref="DeliberaMeter.DebateDuration" />
   ///    histogram and increments <see cref="DeliberaMeter.DebatesCompleted" />.
   /// </summary>
   /// <param name="durationMs">Total debate duration in milliseconds.</param>
   /// <param name="strategyName">Strategy name (tag).</param>
   /// <param name="success">Whether the debate completed successfully.</param>
   public static void RecordDebateCompleted(double durationMs, string strategyName, bool success)
   {
      DeliberaMeter.DebateDuration.Record(durationMs);
      DeliberaMeter.DebatesCompleted.Add(
         1,
         new KeyValuePair<string, object?>(DeliberaTelemetryTags.StrategyName, strategyName),
         new KeyValuePair<string, object?>(DeliberaTelemetryTags.Success, success
            ? "true"
            : "false"));
   }

   /// <summary>
   ///    Records a per-round duration in the <see cref="DeliberaMeter.RoundDuration" /> histogram.
   /// </summary>
   /// <param name="roundNumber">1-based round number.</param>
   /// <param name="durationMs">Round duration in milliseconds.</param>
   public static void RecordRoundDuration(int roundNumber, double durationMs)
   {
      DeliberaMeter.RoundDuration.Record(
         durationMs,
         new KeyValuePair<string, object?>(DeliberaTelemetryTags.RoundNumber, roundNumber));
   }

   /// <summary>
   ///    Records token usage in the <see cref="DeliberaMeter.TokensTotal" /> counter.
   /// </summary>
   /// <param name="memberName">Council member display name (tag).</param>
   /// <param name="direction">Token direction: <c>"input"</c> or <c>"output"</c>.</param>
   /// <param name="tokenCount">Number of tokens to add.</param>
   public static void RecordTokens(string memberName, string direction, long tokenCount)
   {
      if (tokenCount <= 0) return;
      DeliberaMeter.TokensTotal.Add(
         tokenCount,
         new KeyValuePair<string, object?>(DeliberaTelemetryTags.MemberName, memberName),
         new KeyValuePair<string, object?>(DeliberaTelemetryTags.TokenDirection, direction));
   }

   /// <summary>
   ///    Records the most recent compression ratio in the
   ///    <see cref="DeliberaMeter.CompressionRatio" /> gauge.
   /// </summary>
   /// <param name="ratio">Compression ratio (compressed / original). <c>0.5</c> = halved.</param>
   public static void RecordCompressionRatio(double ratio)
   {
      DeliberaMeter.CompressionRatio.Record(ratio);
   }

   /// <summary>
   ///    Marks an activity as failed with an error tag and status.
   ///    No-op when <paramref name="activity" /> is <c>null</c>.
   /// </summary>
   public static void MarkFailed(Activity? activity, string errorMessage)
   {
      if (activity is null) return;
      activity.SetStatus(ActivityStatusCode.Error, errorMessage);
      activity.SetTag(DeliberaTelemetryTags.Success, "false");
      activity.SetTag(DeliberaTelemetryTags.ErrorMessage, errorMessage);
   }

   /// <summary>
   ///    Marks an activity as successful (OK status).
   ///    No-op when <paramref name="activity" /> is <c>null</c>.
   /// </summary>
   public static void MarkSucceeded(Activity? activity)
   {
      if (activity is null) return;
      activity.SetStatus(ActivityStatusCode.Ok);
      activity.SetTag(DeliberaTelemetryTags.Success, "true");
   }
}
