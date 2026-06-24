namespace Delibera.Core.Models;

/// <summary>
///    Severity level for execution log entries.
/// </summary>
/// <remarks>
///    Renamed from <c>LogLevel</c> in v10.2 to avoid a name clash with
///    <see cref="Microsoft.Extensions.Logging.LogLevel" />, which is now the canonical
///    logging severity used throughout Delibera when <see cref="Microsoft.Extensions.Logging.ILogger" />
///    is wired up.
/// </remarks>
public enum ExecutionLogLevel
{
   /// <summary>Detailed trace-level messages for debugging.</summary>
   Trace = 0,

   /// <summary>Informational messages about normal operations.</summary>
   Info = 1,

   /// <summary>Warning messages about potential issues.</summary>
   Warning = 2,

   /// <summary>Error messages about failures.</summary>
   Error = 3
}

/// <summary>
///    A single execution log entry recorded during council debate execution.
///    Captures Chairman actions, Knowledge Keeper queries, compression operations,
///    participant responses, and other significant events.
/// </summary>
/// <param name="Level">Severity level of the log entry.</param>
/// <param name="Source">
///    Component that generated the log (e.g., "Chairman", "KnowledgeKeeper", "Compression",
///    "Participant").
/// </param>
/// <param name="Message">Human-readable description of the event.</param>
/// <param name="Timestamp">UTC timestamp when the event occurred.</param>
public sealed record ExecutionLog(
   ExecutionLogLevel Level,
   string Source,
   string Message,
   DateTime Timestamp)
{
   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="ExecutionLogLevel.Info" /> and the current UTC time.
   /// </summary>
   public static ExecutionLog Info(string source, string message)
   {
      return new ExecutionLog(ExecutionLogLevel.Info, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="ExecutionLogLevel.Trace" /> and the current UTC time.
   /// </summary>
   public static ExecutionLog Trace(string source, string message)
   {
      return new ExecutionLog(ExecutionLogLevel.Trace, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="ExecutionLogLevel.Warning" /> and the current UTC time.
   /// </summary>
   public static ExecutionLog Warn(string source, string message)
   {
      return new ExecutionLog(ExecutionLogLevel.Warning, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="ExecutionLogLevel.Error" /> and the current UTC time.
   /// </summary>
   public static ExecutionLog Error(string source, string message)
   {
      return new ExecutionLog(ExecutionLogLevel.Error, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Maps this execution-log level to the equivalent
   ///    <see cref="Microsoft.Extensions.Logging.LogLevel" /> used by <see cref="Microsoft.Extensions.Logging.ILogger" />.
   /// </summary>
   public Microsoft.Extensions.Logging.LogLevel ToMicrosoftLogLevel()
   {
      return Level switch
      {
         ExecutionLogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
         ExecutionLogLevel.Info => Microsoft.Extensions.Logging.LogLevel.Information,
         ExecutionLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
         ExecutionLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
         _ => Microsoft.Extensions.Logging.LogLevel.None
      };
   }

   /// <summary>
   ///    Formats the log entry as a single-line string.
   /// </summary>
   public override string ToString()
   {
      return $"[{Timestamp:HH:mm:ss.fff}] [{Level,-7}] [{Source}] {Message}";
   }
}