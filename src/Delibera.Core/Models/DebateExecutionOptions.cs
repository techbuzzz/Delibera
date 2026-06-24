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
public sealed record DebateExecutionOptions(
   string? ResponseLanguage = null,
   int MaxDegreeOfParallelism = 0,
   ILogger? Logger = null)
{
   /// <summary>Singleton representing "no extra execution options" (legacy behaviour).</summary>
   public static DebateExecutionOptions Default { get; } = new();

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