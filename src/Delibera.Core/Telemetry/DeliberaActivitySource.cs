using System.Diagnostics;

namespace Delibera.Core.Telemetry;

/// <summary>
///    Centralised <see cref="ActivitySource" /> for Delibera trace spans.
///    All instrumentation code calls <c>StartActivity</c> which returns
///    <c>null</c> when no <see cref="ActivityListener" /> is attached — the standard
///    .NET zero-overhead pattern.
/// </summary>
/// <remarks>
///    <para>
///       The activity-source name defaults to <see cref="DefaultName" /> but can be
///       overridden via <see cref="TelemetryOptions.ActivitySourceName" /> when wiring
///       through DI. When the caller does that, they should also call
///       <c>OpenTelemetry.WithTracing(b =&gt; b.AddSource(options.ActivitySourceName))</c>.
///    </para>
///    <para>
///       <b>Span hierarchy</b> emitted by Delibera:
///       <code>
/// delibera.council.execute
/// ├── delibera.council.round          (per round, tag: round_number)
/// │   ├── delibera.member.respond     (per participant, tag: member_name)
/// │   ├── delibera.rag.query          (if Knowledge Keeper is attached)
/// │   ├── delibera.compression        (if compression is enabled)
/// │   └── delibera.operator.execute   (per Operator task, tag: requester_name)
/// └── delibera.chairman.synthesize
///       </code>
///    </para>
/// </remarks>
public static class DeliberaActivitySource
{
   /// <summary>Default activity-source name used when no override is configured.</summary>
   public const string DefaultName = "Delibera.Council";

   private static ActivitySource? _instance;

   /// <summary>
   ///    The current <see cref="ActivitySource" />. Lazily initialised on first access
   ///    using <see cref="DefaultName" /> and version <c>"1.0.0"</c>. Use
   ///    <see cref="Configure(string, string)" /> to override before any debate runs.
   /// </summary>
   public static ActivitySource Instance => _instance ??= new ActivitySource(DefaultName, "1.0.0");

   /// <summary>
   ///    Configures the activity-source name and version. Call once at startup
   ///    (before any debate execution) when wiring through DI with a non-default
   ///    <see cref="TelemetryOptions.ActivitySourceName" />.
   /// </summary>
   /// <param name="name">Activity-source name (must match the OpenTelemetry <c>AddSource</c> call).</param>
   /// <param name="version">Optional service version. Defaults to <c>"1.0.0"</c>.</param>
   public static void Configure(string name, string version = "1.0.0")
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(name);
      // Dispose any previously-created source so listeners re-subscribe to the new one.
      _instance?.Dispose();
      _instance = new ActivitySource(name, version);
   }

   /// <summary>
   ///    Starts a new <see cref="Activity" /> of kind <see cref="ActivityKind.Internal" />
   ///    with the given <paramref name="operationName" />. Returns <c>null</c> when no
   ///    listener is attached (zero-overhead path).
   /// </summary>
   /// <param name="operationName">
   ///    Fully-qualified operation name, e.g. <c>"delibera.council.execute"</c>.
   /// </param>
   /// <param name="tags">Optional initial tags keyed by tag name.</param>
   /// <returns>The started activity, or <c>null</c> when no listener is attached.</returns>
   public static Activity? StartActivity(string operationName, IEnumerable<KeyValuePair<string, object?>>? tags = null)
   {
      var activity = Instance.StartActivity(operationName);
      if (activity is null) return null;

      if (tags is not null)
         foreach (var (key, value) in tags)
            activity.SetTag(key, value);

      return activity;
   }

   /// <summary>
   ///    Starts a new activity with the standard <c>delibera.*</c> prefix and a single tag.
   /// </summary>
   public static Activity? StartActivity(string operationName, string tagKey, object? tagValue)
   {
      return StartActivity(operationName, [new KeyValuePair<string, object?>(tagKey, tagValue)]);
   }

   /// <summary>
   ///    Disposes the underlying <see cref="ActivitySource" />. Intended for tests
   ///    and host shutdown. Subsequent calls to <see cref="Instance" /> will recreate it.
   /// </summary>
   public static void Shutdown()
   {
      _instance?.Dispose();
      _instance = null;
   }
}

/// <summary>
///    Well-known Delibera telemetry operation names (span names).
///    Keep in sync with the activity hierarchy documented on
///    <see cref="DeliberaActivitySource" />.
/// </summary>
public static class DeliberaActivityNames
{
   /// <summary>Top-level debate execution span.</summary>
   public const string CouncilExecute = "delibera.council.execute";

   /// <summary>Per-round span. Tag: <c>round_number</c>.</summary>
   public const string CouncilRound = "delibera.council.round";

   /// <summary>Per-participant response span. Tag: <c>member_name</c>.</summary>
   public const string MemberRespond = "delibera.member.respond";

   /// <summary>RAG query span (Knowledge Keeper). Tag: <c>query</c>.</summary>
   public const string RagQuery = "delibera.rag.query";

   /// <summary>Context compression span. Tag: <c>strategy</c>.</summary>
   public const string Compression = "delibera.compression";

   /// <summary>Operator task execution span. Tag: <c>requester_name</c>.</summary>
   public const string OperatorExecute = "delibera.operator.execute";

   /// <summary>Chairman final-verdict synthesis span.</summary>
   public const string ChairmanSynthesize = "delibera.chairman.synthesize";

   /// <summary>Chairman opening-statement span.</summary>
   public const string ChairmanOpen = "delibera.chairman.open";

   /// <summary>Checkpoint save span (F-03 Persistence). Tag: <c>debate_id</c>.</summary>
   public const string PersistenceSaveCheckpoint = "delibera.persistence.save_checkpoint";

   /// <summary>Checkpoint load span (F-03 Persistence). Tag: <c>debate_id</c>.</summary>
   public const string PersistenceLoadCheckpoint = "delibera.persistence.load_checkpoint";
}

/// <summary>
///    Well-known tag keys used across Delibera spans and metrics.
/// </summary>
public static class DeliberaTelemetryTags
{
   /// <summary>Debate identifier (<see cref="Models.DebateResult.DebateId" />).</summary>
   public const string DebateId = "debate.id";

   /// <summary>Strategy name (<see cref="Interfaces.IDebateStrategy.StrategyName" />).</summary>
   public const string StrategyName = "debate.strategy";

   /// <summary>Round number (1-based).</summary>
   public const string RoundNumber = "round.number";

   /// <summary>Round display name.</summary>
   public const string RoundName = "round.name";

   /// <summary>Council member display name.</summary>
   public const string MemberName = "member.name";

   /// <summary>Member role label.</summary>
   public const string MemberRole = "member.role";

   /// <summary>Compression strategy name.</summary>
   public const string CompressionStrategy = "compression.strategy";

   /// <summary>Operator task requester display name.</summary>
   public const string RequesterName = "operator.requester";

   /// <summary>Operator tool name.</summary>
   public const string ToolName = "operator.tool";

   /// <summary>RAG query text (truncated).</summary>
   public const string RagQuery = "rag.query";

   /// <summary>Token direction: <c>"input"</c> or <c>"output"</c>.</summary>
   public const string TokenDirection = "token.direction";

   /// <summary>Whether the operation succeeded (<c>"true"</c>/<c>"false"</c>).</summary>
   public const string Success = "success";

   /// <summary>Error message when <see cref="Success" /> is <c>"false"</c>.</summary>
   public const string ErrorMessage = "error.message";
}
