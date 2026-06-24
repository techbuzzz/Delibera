namespace Delibera.Core.Models;

/// <summary>
///    Complete debate result — rounds, verdict, execution logs, and metadata.
/// </summary>
public sealed record DebateResult
{
   /// <summary>Unique debate identifier.</summary>
   public string DebateId { get; init; } = Guid.NewGuid().ToString("N");

   /// <summary>Strategy name used for this debate.</summary>
   public required string StrategyName { get; init; }

   /// <summary>Prompt context used in the debate.</summary>
   public required PromptContext Context { get; init; }

   /// <summary>Display names of all participants.</summary>
   public required IReadOnlyList<string> Participants { get; init; }

   /// <summary>Chairman name (if assigned).</summary>
   public string? ChairmanName { get; init; }

   /// <summary>Knowledge Keeper name (if assigned).</summary>
   public string? KnowledgeKeeperName { get; init; }

   /// <summary>Operator name (if assigned).</summary>
   public string? OperatorName { get; init; }

   /// <summary>All debate rounds.</summary>
   public IReadOnlyList<DebateRound> Rounds { get; init; } = [];

   /// <summary>Chairman's opening statement.</summary>
   public string? OpeningStatement { get; init; }

   /// <summary>Chairman's final verdict.</summary>
   public string? FinalVerdict { get; init; }

   /// <summary>Timestamp when the debate started.</summary>
   public DateTime StartedAt { get; init; } = DateTime.UtcNow;

   /// <summary>Timestamp when the debate completed.</summary>
   public DateTime? CompletedAt { get; init; }

   /// <summary>Total debate duration.</summary>
   public TimeSpan TotalDuration => (CompletedAt ?? DateTime.UtcNow) - StartedAt;

   /// <summary>Whether the debate has completed.</summary>
   public bool IsCompleted => CompletedAt.HasValue;

   /// <summary>Token usage statistics (populated when compression is enabled).</summary>
   public TokenStatistics? TokenStats { get; init; }

   /// <summary>Compression operations log (populated when compression is enabled).</summary>
   public IReadOnlyList<CompressionLog> CompressionLogs { get; init; } = [];

   /// <summary>Execution logs captured during debate execution.</summary>
   public IReadOnlyList<ExecutionLog> ExecutionLogs { get; init; } = [];

   // ──────────────────────────────────────────────
   // Markdown export
   // ──────────────────────────────────────────────

   /// <summary>
   ///    Exports the full debate result to Markdown (result content only, without statistics or logs).
   /// </summary>
   public string ToMarkdown()
   {
      var sb = new StringBuilder();

      sb.AppendLine($"# Delibera Debate — {StartedAt:yyyy-MM-dd HH:mm:ss UTC}");
      sb.AppendLine();
      sb.AppendLine($"**Debate ID:** `{DebateId}`");
      sb.AppendLine($"**Strategy:** {StrategyName}");
      sb.AppendLine($"**Duration:** {TotalDuration:mm\\:ss}");
      sb.AppendLine();

      sb.AppendLine("## Input Context");
      sb.AppendLine();
      sb.AppendLine($"**System Prompt:** {Context.SystemPrompt}");
      sb.AppendLine();
      sb.AppendLine($"**User Prompt:** {Context.UserPrompt}");
      sb.AppendLine();

      if (Context.KnowledgeFiles is { Count: > 0 })
         sb.AppendLine($"**Knowledge Files:** {string.Join(", ", Context.KnowledgeFiles)}");

      if (!string.IsNullOrWhiteSpace(Context.KnowledgeContent))
      {
         sb.AppendLine();
         sb.AppendLine("### Knowledge Context");
         sb.AppendLine();
         sb.AppendLine(Context.KnowledgeContent);
      }

      sb.AppendLine();
      sb.AppendLine($"**Participants:** {string.Join(", ", Participants)}");
      if (!string.IsNullOrEmpty(ChairmanName))
         sb.AppendLine($"**Chairman:** {ChairmanName}");
      if (!string.IsNullOrEmpty(KnowledgeKeeperName))
         sb.AppendLine($"**Knowledge Keeper:** {KnowledgeKeeperName}");
      if (!string.IsNullOrEmpty(OperatorName))
         sb.AppendLine($"**Operator:** {OperatorName}");
      sb.AppendLine();

      // Opening statement
      if (!string.IsNullOrWhiteSpace(OpeningStatement))
      {
         sb.AppendLine("## Opening Statement (Chairman)");
         sb.AppendLine();
         sb.AppendLine(OpeningStatement);
         sb.AppendLine();
      }

      // Rounds
      foreach (var round in Rounds)
      {
         sb.AppendLine($"## Round {round.RoundNumber}: {round.RoundName}");
         sb.AppendLine();
         if (!string.IsNullOrEmpty(round.Description))
            sb.AppendLine($"*{round.Description}*").AppendLine();

         // Knowledge interactions
         if (round.KnowledgeInteractions is { Count: > 0 })
         {
            sb.AppendLine("### 📚 Knowledge Keeper Interactions");
            sb.AppendLine();
            foreach (var ki in round.KnowledgeInteractions)
            {
               sb.AppendLine($"> **Q:** {ki.Query}");
               sb.AppendLine($"> **A:** {ki.Answer} *({ki.SourceChunks} chunks)*");
               sb.AppendLine();
            }
         }

         // Operator interactions
         if (round.OperatorInteractions is { Count: > 0 })
         {
            sb.AppendLine("### 🛠️ Operator Interactions");
            sb.AppendLine();
            foreach (var oi in round.OperatorInteractions)
            {
               var tools = oi.ToolCalls.Count > 0
                  ? string.Join(", ", oi.ToolCalls.Select(c => $"`{c.ServerName}.{c.ToolName}`"))
                  : "none";
               sb.AppendLine($"> **{oi.RequesterName}** requested: {oi.Task}");
               sb.AppendLine($"> **Tools used:** {tools}");
               sb.AppendLine($"> **Answer:** {oi.Answer}{(oi.Compressed ? " *(compressed)*" : "")}");
               sb.AppendLine();
            }
         }

         foreach (var (member, response) in round.Responses)
         {
            sb.AppendLine($"### {member}");
            sb.AppendLine();
            sb.AppendLine(response);
            sb.AppendLine();
         }
      }

      // Final Verdict
      if (!string.IsNullOrWhiteSpace(FinalVerdict))
      {
         sb.AppendLine("## Final Verdict (Chairman)");
         sb.AppendLine();
         sb.AppendLine(FinalVerdict);
         sb.AppendLine();
      }

      sb.AppendLine("---");
      sb.AppendLine($"*Generated by Delibera Framework v3.1 at {CompletedAt ?? DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}*");

      return sb.ToString();
   }

   /// <summary>
   ///    Exports token statistics and compression logs to Markdown.
   /// </summary>
   public string ToStatisticsMarkdown()
   {
      var sb = new StringBuilder();

      sb.AppendLine($"# 📊 Debate Statistics — {DebateId}");
      sb.AppendLine();
      sb.AppendLine($"**Debate ID:** `{DebateId}`");
      sb.AppendLine($"**Strategy:** {StrategyName}");
      sb.AppendLine($"**Duration:** {TotalDuration:mm\\:ss}");
      sb.AppendLine($"**Rounds:** {Rounds.Count}");
      sb.AppendLine($"**Participants:** {Participants.Count}");
      sb.AppendLine();

      if (TokenStats is not null)
         MarkdownRender.WriteTokenStatistics(sb, TokenStats);

      if (CompressionLogs is { Count: > 0 })
      {
         sb.AppendLine("## 🗜️ Compression Log");
         sb.AppendLine();
         foreach (var log in CompressionLogs)
            sb.AppendLine($"- **Round {log.RoundNumber}** — {log.Description}: {log.OriginalTokens:N0} → {log.CompressedTokens:N0} tokens ({log.Ratio:P0}) via *{log.StrategyName}* ({log.Duration.TotalMilliseconds:F0}ms)");
         sb.AppendLine();
      }

      sb.AppendLine("---");
      sb.AppendLine($"*Generated by Delibera Framework v3.1 at {CompletedAt ?? DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}*");

      return sb.ToString();
   }

   /// <summary>
   ///    Exports execution logs to Markdown.
   /// </summary>
   public string ToLogsMarkdown()
   {
      var sb = new StringBuilder();

      sb.AppendLine($"# 📋 Execution Logs — {DebateId}");
      sb.AppendLine();
      sb.AppendLine($"**Debate ID:** `{DebateId}`");
      sb.AppendLine($"**Started:** {StartedAt:yyyy-MM-dd HH:mm:ss UTC}");
      sb.AppendLine($"**Completed:** {CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "In Progress"}");
      sb.AppendLine($"**Total Entries:** {ExecutionLogs.Count}");
      sb.AppendLine();

      if (ExecutionLogs is { Count: > 0 })
      {
         sb.AppendLine("## Log Entries");
         sb.AppendLine();
         sb.AppendLine("| Time | Level | Source | Message |");
         sb.AppendLine("|------|-------|--------|---------|");
         foreach (var log in ExecutionLogs)
         {
            var icon = log.Level switch
            {
               ExecutionLogLevel.Trace => "🔍",
               ExecutionLogLevel.Info => "ℹ️",
               ExecutionLogLevel.Warning => "⚠️",
               ExecutionLogLevel.Error => "❌",
               _ => "•"
            };
            sb.AppendLine($"| {log.Timestamp:HH:mm:ss.fff} | {icon} {log.Level} | {log.Source} | {log.Message} |");
         }

         sb.AppendLine();

         // Summary by source
         sb.AppendLine("## Summary by Source");
         sb.AppendLine();
         foreach (var group in ExecutionLogs.GroupBy(l => l.Source).OrderBy(g => g.Key))
            sb.AppendLine($"- **{group.Key}**: {group.Count()} entries ({group.Count(l => l.Level == ExecutionLogLevel.Error)} errors, {group.Count(l => l.Level == ExecutionLogLevel.Warning)} warnings)");
         sb.AppendLine();
      }
      else
      {
         sb.AppendLine("*No execution logs recorded.*");
         sb.AppendLine();
      }

      sb.AppendLine("---");
      sb.AppendLine($"*Generated by Delibera Framework v3.1 at {CompletedAt ?? DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}*");

      return sb.ToString();
   }

   // ──────────────────────────────────────────────
   // File saving
   // ──────────────────────────────────────────────

   /// <summary>
   ///    Saves the debate result (rounds and verdict) to a Markdown file.
   /// </summary>
   /// <param name="filePath">Path for the result Markdown file.</param>
   public Task SaveToMarkdownAsync(string filePath)
   {
      return WriteAllTextAsync(filePath, ToMarkdown());
   }

   /// <summary>
   ///    Saves token statistics and compression logs to a Markdown file.
   /// </summary>
   /// <param name="filePath">Path for the statistics Markdown file.</param>
   public Task SaveStatisticsAsync(string filePath)
   {
      return WriteAllTextAsync(filePath, ToStatisticsMarkdown());
   }

   /// <summary>
   ///    Saves execution logs to a Markdown file.
   /// </summary>
   /// <param name="filePath">Path for the logs Markdown file.</param>
   public Task SaveLogsAsync(string filePath)
   {
      return WriteAllTextAsync(filePath, ToLogsMarkdown());
   }

   /// <summary>
   ///    Saves all three files (result.md, statistics.md, logs.md) to the specified directory.
   /// </summary>
   /// <param name="outputDirectory">Directory where files will be created.</param>
   /// <param name="filePrefix">Optional prefix for file names (default: "debate").</param>
   /// <returns>Paths of the three created files.</returns>
   public async Task<(string ResultPath, string StatisticsPath, string LogsPath)> SaveAllAsync(
      string outputDirectory,
      string? filePrefix = null)
   {
      if (!Directory.Exists(outputDirectory))
         Directory.CreateDirectory(outputDirectory);

      var prefix = filePrefix ?? $"debate_{StartedAt:yyyyMMdd_HHmmss}";
      var resultPath = Path.Combine(outputDirectory, $"{prefix}_result.md");
      var statsPath = Path.Combine(outputDirectory, $"{prefix}_statistics.md");
      var logsPath = Path.Combine(outputDirectory, $"{prefix}_logs.md");

      await Task.WhenAll(
         SaveToMarkdownAsync(resultPath),
         SaveStatisticsAsync(statsPath),
         SaveLogsAsync(logsPath));

      return (resultPath, statsPath, logsPath);
   }

   /// <summary>
   ///    Saves the debate result to a Markdown file (backward-compatible).
   /// </summary>
   /// <param name="filePath">Path for the output file.</param>
   public async Task SaveToFileAsync(string filePath)
   {
      // Backward compatible: saves the full content (result + statistics + logs) in a single file
      var sb = new StringBuilder();
      sb.Append(ToMarkdown());
      sb.AppendLine();

      if (TokenStats is not null || CompressionLogs is { Count: > 0 })
      {
         sb.AppendLine();
         // Inline statistics (same as v3.0 behavior)
         if (TokenStats is not null)
         {
            sb.AppendLine("## 📊 Token Statistics");
            sb.AppendLine();
            MarkdownRender.WriteTokenStatistics(sb, TokenStats);
         }

         if (CompressionLogs is { Count: > 0 })
         {
            sb.AppendLine("### 🗜️ Compression Log");
            sb.AppendLine();
            foreach (var log in CompressionLogs)
               sb.AppendLine($"- **Round {log.RoundNumber}** — {log.Description}: {log.OriginalTokens:N0} → {log.CompressedTokens:N0} tokens ({log.Ratio:P0}) via *{log.StrategyName}* ({log.Duration.TotalMilliseconds:F0}ms)");
            sb.AppendLine();
         }
      }

      await WriteAllTextAsync(filePath, sb.ToString());
   }

   private static async Task WriteAllTextAsync(string filePath, string content)
   {
      var directory = Path.GetDirectoryName(filePath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
         Directory.CreateDirectory(directory);

      await File.WriteAllTextAsync(filePath, content);
   }
}

/// <summary>
///    Internal rendering helpers shared by markdown export methods.
/// </summary>
internal static class MarkdownRender
{
   public static void WriteTokenStatistics(StringBuilder sb, TokenStatistics stats)
   {
      sb.AppendLine("| Metric | Value |");
      sb.AppendLine("|--------|------:|");
      sb.AppendLine($"| Original Tokens | {stats.TotalOriginalTokens:N0} |");
      sb.AppendLine($"| Compressed Tokens | {stats.TotalCompressedTokens:N0} |");
      sb.AppendLine($"| Response Tokens | {stats.TotalResponseTokens:N0} |");
      sb.AppendLine($"| **Tokens Saved** | **{stats.TokensSaved:N0} ({stats.SavedPercent:F1}%)** |");
      sb.AppendLine($"| Compression Ratio | {stats.OverallCompressionRatio:P1} |");
      sb.AppendLine($"| Grand Total | {stats.GrandTotal:N0} |");
      sb.AppendLine();

      if (stats.RoundBreakdown is { Count: > 0 })
      {
         sb.AppendLine("### Per-Round Breakdown");
         sb.AppendLine();
         sb.AppendLine("| Round | Original | Compressed | Responses | Strategy |");
         sb.AppendLine("|-------|----------|------------|-----------|----------|");
         foreach (var rb in stats.RoundBreakdown)
            sb.AppendLine($"| {rb.RoundNumber}. {rb.RoundName} | {rb.OriginalTokens:N0} | {rb.CompressedTokens:N0} | {rb.ResponseTokens:N0} | {rb.CompressionStrategy} |");
         sb.AppendLine();
      }
   }
}