namespace Delibera.Core.Models;

/// <summary>
///    Severity level for execution log entries.
/// </summary>
public enum LogLevel
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
   LogLevel Level,
   string Source,
   string Message,
   DateTime Timestamp)
{
   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="LogLevel.Info" /> and the current UTC time.
   /// </summary>
   /// <param name="source">Component source name.</param>
   /// <param name="message">Log message.</param>
   public static ExecutionLog Info(string source, string message)
   {
      return new ExecutionLog(LogLevel.Info, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="LogLevel.Trace" /> and the current UTC time.
   /// </summary>
   /// <param name="source">Component source name.</param>
   /// <param name="message">Log message.</param>
   public static ExecutionLog Trace(string source, string message)
   {
      return new ExecutionLog(LogLevel.Trace, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="LogLevel.Warning" /> and the current UTC time.
   /// </summary>
   /// <param name="source">Component source name.</param>
   /// <param name="message">Log message.</param>
   public static ExecutionLog Warn(string source, string message)
   {
      return new ExecutionLog(LogLevel.Warning, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Creates an <see cref="ExecutionLog" /> with <see cref="LogLevel.Error" /> and the current UTC time.
   /// </summary>
   /// <param name="source">Component source name.</param>
   /// <param name="message">Log message.</param>
   public static ExecutionLog Error(string source, string message)
   {
      return new ExecutionLog(LogLevel.Error, source, message, DateTime.UtcNow);
   }

   /// <summary>
   ///    Formats the log entry as a single-line string.
   /// </summary>
   public override string ToString()
   {
      return $"[{Timestamp:HH:mm:ss.fff}] [{Level,-7}] [{Source}] {Message}";
   }
}