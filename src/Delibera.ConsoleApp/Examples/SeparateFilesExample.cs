using Delibera.Core.Models;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
/// Demonstrates the separate file saving feature of <see cref="DebateResult"/>.
/// Shows how to save result, statistics, and logs to individual Markdown files.
/// </summary>
public static class SeparateFilesExample
{
   /// <summary>
   /// Runs the separate files example with a mock <see cref="DebateResult"/>.
   /// </summary>
   public static async Task RunAsync()
   {
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  📁 Separate Files Example (v3.1)");
      Console.WriteLine("═══════════════════════════════════════════\n");

      // Create a sample DebateResult with mock data
      var result = new DebateResult
      {
         StrategyName = "Standard Debate",
         Context = new PromptContext
         {
            SystemPrompt = "You are a helpful AI assistant.",
            UserPrompt = "Compare microservices vs monoliths."
         },
         Participants = ["llama2", "qwen2.5"],
         ChairmanName = "qwen2.5",
         Rounds =
          [
              new DebateRound
                {
                    RoundNumber = 1,
                    RoundName = "Initial Arguments",
                    Description = "Each participant presents their initial position.",
                    Responses = new Dictionary<string, string>
                    {
                        ["llama2"] = "Microservices offer better scalability...",
                        ["qwen2.5"] = "Monoliths are simpler to develop and deploy..."
                    }
                },
                new DebateRound
                {
                    RoundNumber = 2,
                    RoundName = "Critique",
                    Description = "Participants critique each other's arguments.",
                    Responses = new Dictionary<string, string>
                    {
                        ["llama2"] = "While simplicity is valuable, at scale...",
                        ["qwen2.5"] = "Scalability gains must be weighed against complexity..."
                    }
                }
          ],
         FinalVerdict = "Both approaches have merits. Use microservices for large teams and scale; monoliths for simplicity.",
         CompletedAt = DateTime.UtcNow,
         TokenStats = new TokenStatistics
         {
            TotalOriginalTokens = 5000,
            TotalCompressedTokens = 3500,
            TotalResponseTokens = 2000,
            RoundBreakdown =
              [
                  new RoundTokenUsage(1, "Initial Arguments", 2500, 1750, 1000, "Hybrid"),
                    new RoundTokenUsage(2, "Critique", 2500, 1750, 1000, "Hybrid")
              ]
         },
         ExecutionLogs =
          [
              ExecutionLog.Info("Council", "Starting debate — strategy: Standard Debate, members: 2"),
                ExecutionLog.Info("Chairman", "Chairman assigned: qwen2.5"),
                ExecutionLog.Info("Council", "Round 1 completed: Initial Arguments (2.1s, 2 responses)"),
                ExecutionLog.Trace("Participant", "llama2 responded (245 chars)"),
                ExecutionLog.Trace("Participant", "qwen2.5 responded (312 chars)"),
                ExecutionLog.Info("Council", "Round 2 completed: Critique (1.8s, 2 responses)"),
                ExecutionLog.Info("Council", "Debate completed — 2 rounds, duration: 5.2s"),
                ExecutionLog.Info("Compression", "Token stats — original: 5,000, compressed: 3,500, saved: 30.0%")
          ]
      };

      // ── Save as separate files ──
      var outputDir = Path.Combine(".", "debate_results", "separate_files_demo");
      Console.WriteLine($"📂 Output directory: {outputDir}\n");

      var (resultPath, statsPath, logsPath) = await result.SaveAllAsync(outputDir, "demo");

      Console.WriteLine("✅ Files created:");
      Console.WriteLine($"   📄 Result:     {resultPath}");
      Console.WriteLine($"   📊 Statistics:  {statsPath}");
      Console.WriteLine($"   📋 Logs:        {logsPath}");

      // ── Also demonstrate individual save methods ──
      Console.WriteLine("\n── Individual save methods ──\n");

      var singleDir = Path.Combine(outputDir, "individual");
      await result.SaveToMarkdownAsync(Path.Combine(singleDir, "result.md"));
      await result.SaveStatisticsAsync(Path.Combine(singleDir, "statistics.md"));
      await result.SaveLogsAsync(Path.Combine(singleDir, "logs.md"));

      Console.WriteLine("✅ Individual files created in: " + singleDir);

      // ── Show file sizes ──
      Console.WriteLine("\n── File sizes ──\n");
      foreach (var path in new[] { resultPath, statsPath, logsPath })
      {
         var info = new FileInfo(path);
         Console.WriteLine($"   {Path.GetFileName(path)}: {info.Length:N0} bytes");
      }

      // ── Preview logs markdown ──
      Console.WriteLine("\n── Logs Markdown Preview ──\n");
      var logsPreview = result.ToLogsMarkdown();
      var lines = logsPreview.Split('\n');
      foreach (var line in lines.Take(20))
         Console.WriteLine($"  {line}");
      if (lines.Length > 20)
         Console.WriteLine($"  ... ({lines.Length - 20} more lines)");

      Console.WriteLine("\n🏁 Separate files example complete.\n");
   }
}
