using Delibera.Core.Compression;
using Microsoft.Extensions.Logging;

namespace Delibera.Core.Models;

/// <summary>
///    Per-execution options threaded through debate strategies alongside the
///    <see cref="IDebateStrategy" /> call. Bundles the response-language
///    directive, parallelism budget, and optional <see cref="ILogger" /> so every
///    strategy and helper can act on them without growing the public strategy signature
///    one parameter at a time.
/// </summary>
/// <param name="ResponseLanguage">
///    When non-empty, every model response (participants, Chairman, Knowledge Keeper,
///    Operator) is forced into this language via a strict prompt directive.
/// </param>
/// <param name="MaxDegreeOfParallelism">
///    Maximum number of concurrent operations within a debate round (Operator task
///    delegation, parallel Knowledge Keeper queries). <c>0</c> = unbounded.
/// </param>
/// <param name="Logger">
///    Optional <see cref="ILogger" /> used by the executor and strategies to surface
///    progress to a host's logging pipeline. <c>null</c> disables structured logging.
/// </param>
/// <param name="ContextCompressor">
///    Compressor applied to each round prompt before it is sent to the members. Populated by
///    <c>CouncilExecutor</c> from the council's configured compressor; the round-prompt path is
///    the only place in the pipeline that calls it.
/// </param>
/// <param name="ContextCompressionOptions">Options passed to <paramref name="ContextCompressor" />.</param>
/// <param name="ContextCompressionCache">Optional cache for repeated prompts.</param>
/// <param name="CompressionLogs">
///    Sink for <see cref="CompressionLog" /> entries produced during the run. Shared by reference so
///    that every round appends to the list the executor publishes on the result.
/// </param>
public sealed record DebateExecutionOptions(
   string? ResponseLanguage = null,
   int MaxDegreeOfParallelism = 0,
   ILogger? Logger = null,
   IContextCompressor? ContextCompressor = null,
   CompressionOptions? ContextCompressionOptions = null,
   CompressionCache? ContextCompressionCache = null,
   List<CompressionLog>? CompressionLogs = null)
{
   /// <summary>
   ///   Minimum prompt size, in tokens, before compression is attempted. Compressing a short
   ///   prompt costs more than it saves, and every compressor in the library short-circuits
   ///   below roughly this size anyway.
   /// </summary>
   public int CompressionThresholdTokens { get; init; } = 1_200;

   /// <summary>
   ///   Optional cost gate consulted before every member model call. A denial stops further
   ///   calls and marks the ledger truncated, so the debate returns a degraded result carrying
   ///   the spend so far rather than throwing.
   /// </summary>
   public ICostGate? CostGate { get; init; }

   /// <summary>
   ///   Optional rate limiter consulted before every member model call.
   /// </summary>
   public IRateLimiter? RateLimiter { get; init; }

   /// <summary>
   ///   Optional price list used to turn token counts into money. Without it every cost is
   ///   reported as an estimate at zero, which is what makes an unregistered model visible
   ///   instead of silently free.
   /// </summary>
   public IModelPricingRegistry? PricingRegistry { get; init; }

   /// <summary>
   ///   Optional rate-limit policy. When set and no explicit <see cref="RateLimiter" /> was
   ///   supplied, the executor builds a <see cref="Cost.TokenBucketRateLimiter" /> from it.
   /// </summary>
   public RateLimitPolicy? RateLimitPolicy { get; init; }

   /// <summary>
   ///   Whether per-call accounting is worth doing at all.
   /// </summary>
   /// <remarks>
   ///   <para>
   ///   True when the caller configured anything that consumes a cost: a gate, a limiter or a
   ///   price registry. False means the ledger would be built and filled for nothing — the
   ///   executor publishes <see cref="Models.DebateResult.CostEstimate" /> only in the first
   ///   case.
   ///   </para>
   ///   <para>
   ///   This is a hot-path predicate. Accounting concatenates the prompts and runs the token
   ///   counter over the prompt and the response on every member call, and the prompt grows each
   ///   round as history accumulates, so an unconfigured debate would pay a scanning cost
   ///   quadratic in its own length and discard the result.
   ///   </para>
   /// </remarks>
   public bool CostTrackingEnabled =>
      CostGate is not null || RateLimiter is not null || PricingRegistry is not null;

   /// <summary>
   ///   Shared accumulator of token counts and spend. Left <c>null</c> until a member call
   ///   actually needs it, then created once and reused by every round so that concurrent member
   ///   tasks accumulate into a single estimate.
   /// </summary>
   public Cost.CostLedger? CostLedger
   {
      get => _ledger;
      init => _ledger = value;
   }

   private Cost.CostLedger? _ledger;

   /// <summary>
   ///   Returns the ledger for this run, creating and caching it on first access so that every
   ///   round and every member task shares one instance.
   /// </summary>
   public Cost.CostLedger GetOrCreateLedger()
      => _ledger ??= new Cost.CostLedger(PricingRegistry);

   /// <summary>
   ///   Optional catalogue of tools members may call. A provider supplies the tools; the
   ///   pipeline decides when to invoke them.
   /// </summary>
   public IToolProvider? ToolProvider { get; init; }

   /// <summary>
   ///   Tools handed directly to every member, for callers that already have
   ///   <see cref="AIFunction" /> instances and need no provider abstraction.
   /// </summary>
   public IReadOnlyList<AIFunction>? MemberTools { get; init; }

   /// <summary>
   ///   Maximum tool round-trips per member turn. A model that keeps requesting tools would
   ///   otherwise keep the debate running; three is enough for look-up-then-answer and stops
   ///   the pathological case.
   /// </summary>
   public int MaxToolIterations { get; init; } = 3;

   private IReadOnlyList<AIFunction>? _resolvedTools;

   /// <summary>
   ///   Shared sink for tool calls, reused across rounds so the executor can publish every call
   ///   on the result. Shared by reference for the same reason <c>CompressionLogs</c> is.
   /// </summary>
   public List<ToolCallLog>? ToolCallLog
   {
      get => _toolCalls;
      init => _toolCalls = value;
   }

   private List<ToolCallLog>? _toolCalls;

   /// <summary>Returns the shared tool-call sink, creating it on first use.</summary>
   public List<ToolCallLog> GetOrCreateToolCalls() => _toolCalls ??= [];

   /// <summary>
   ///   Resolves and caches the tool catalogue for this run, so the providers are enumerated
   ///   once rather than on every member call of every round.
   /// </summary>
   /// <param name="ct">Cancellation token forwarded to the providers.</param>
   public async ValueTask<IReadOnlyList<AIFunction>> GetOrCreateToolsAsync(CancellationToken ct = default)
   {
      if (_resolvedTools is not null) return _resolvedTools;

      var tools = new List<AIFunction>();
      if (MemberTools is { Count: > 0 })
         tools.AddRange(MemberTools);

      if (ToolProvider is not null)
         tools.AddRange(await ToolProvider.GetToolsAsync(ct).ConfigureAwait(false));

      _resolvedTools = tools;
      return _resolvedTools;
   }

   /// <summary>Singleton representing "no extra execution options" (legacy behaviour).</summary>
   public static DebateExecutionOptions Default { get; } = new();

   /// <summary>Whether a compressor is available to the strategies.</summary>
   public bool HasCompressor => ContextCompressor is not null;

   /// <summary>Whether a response language directive is configured.</summary>
   public bool HasResponseLanguage => !string.IsNullOrWhiteSpace(ResponseLanguage);

   /// <summary>
   ///    Builds the language-enforcement directive block that is appended to system and
   ///    user prompts. Returns an empty string when no language is configured.
   /// </summary>
   public string BuildLanguageDirective()
   {
      return HasResponseLanguage
         ? $"\n\nIMPORTANT: You MUST answer exclusively in {ResponseLanguage}. Never use any other language, regardless of the language used in the question, retrieved context, or other participants' messages."
         : string.Empty;
   }

   /// <summary>
   ///    Returns a <see cref="ParallelOptions" /> instance reflecting
   ///    <see cref="MaxDegreeOfParallelism" /> (useful for <c>Parallel.ForEachAsync</c>).
   /// </summary>
   public ParallelOptions ToParallelOptions(CancellationToken ct)
   {
      var po = new ParallelOptions { CancellationToken = ct };
      if (MaxDegreeOfParallelism > 0)
         po.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
      return po;
   }
}

/// <summary>
///    Internal helper that bridges <see cref="ExecutionLog" /> entries and a host's
///    <see cref="ILogger" /> so the same event is recorded both in the in-memory log
///    collection and the host's logging pipeline.
/// </summary>
internal static class ExecutionLogSink
{
   /// <summary>
   ///    Forwards the <paramref name="entry" /> to <paramref name="logger" /> using the
   ///    appropriate <see cref="Microsoft.Extensions.Logging.LogLevel" />, then returns the
   ///    same entry so the caller can also append it to its <see cref="ExecutionLog" />
   ///    collection.
   /// </summary>
   public static ExecutionLog Emit(ILogger? logger, ExecutionLog entry)
   {
      if (logger is null) return entry;

      var msLevel = entry.ToMicrosoftLogLevel();
      if (!logger.IsEnabled(msLevel)) return entry;

      // Use a structured log event id of 0 (free-form) with named placeholders so
      // scopes/external sinks can correlate by Source + Message.
      logger.Log(msLevel, new EventId(0, entry.Source), "[{Source}] {Message}", entry.Source, entry.Message);
      return entry;
   }
}
