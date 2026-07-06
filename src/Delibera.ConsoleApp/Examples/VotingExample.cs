using System.Diagnostics;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Voting;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-02 Pluggable Vote / Consensus Engine.
///    A voting Chairman asks each participant to rank the options surfaced during
///    the debate, then tallies the ballots via an <see cref="IVotingStrategy" />.
/// </summary>
public static class VotingExample
{
   /// <summary>Runs the voting demo against any reachable Ollama endpoint.</summary>
   public static async Task RunAsync(CancellationToken ct = default)
   {
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  🗳️ Voting Engine (F-02)");
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  Structured voting as an alternative to Chairman synthesis.");
      Console.WriteLine();

      // ── Provider setup ──
      using var factory = new ProviderFactory();
      OllamaProvider? ollama = null;
      try
      {
         ollama = factory.CreateLocalOllama("http://localhost:11434");
         if (!await ollama.IsAvailableAsync(ct))
         {
            Console.WriteLine("  ⚠️  Local Ollama not available — falling back to Ollama Cloud.");
            ollama.Dispose();
            ollama = null;
         }
      }
      catch (Exception ex)
      {
         Console.WriteLine($"  ⚠️  Could not initialise local Ollama: {ex.Message}");
      }

      if (ollama is null)
      {
         var apiKey = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
         if (string.IsNullOrWhiteSpace(apiKey))
         {
            Console.WriteLine("  ❌ No Ollama provider available (no local server, no OLLAMA_API_KEY env).");
            Console.WriteLine("     Skipping live demo. The voting API is wired regardless —");
            Console.WriteLine("     run against any reachable Ollama to see the tally in action.");
            return;
         }

         ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
      }

      ILLMProvider llm = ollama;

      // ── Build a council with a weighted voting chairman ──
      var votingStrategy = new WeightedVotingStrategy
      {
         MemberWeights = { ["Architect"] = 2.0, ["SecurityExpert"] = 1.5 }
      };

      var executor = new CouncilBuilder()
         .AddMember("llama3.2", llm, "Architect",
            "You are a senior software architect. Propose 2-3 concrete options as a numbered list.")
         .AddMember("qwen2.5", llm, "SecurityExpert",
            "You are a security expert. Propose 2-3 concrete options as a numbered list.")
         .AddMember("mistral", llm, "Pragmatist",
            "You are a pragmatist. Propose 2-3 concrete options as a numbered list.")
         .WithUserPrompt("What architecture should we use for a real-time collaboration app? Propose specific options.")
         .WithMaxRounds(1)
         .WithTemperature(0.5f)
         .WithVotingChairman("qwen2.5", llm, votingStrategy)
         .SaveResultTo("./debate_results/voting_demo.md")
         .Build();

      Console.WriteLine($"  Voting strategy:  {executor.VotingStrategy!.MethodName}");
      Console.WriteLine("  Member weights:   Architect=2.0, SecurityExpert=1.5, Pragmatist=1.0 (default)");
      Console.WriteLine();

      Console.WriteLine("  Starting debate with voting chairman…");
      var sw = Stopwatch.StartNew();
      var result = await executor.ExecuteAsync(ct);
      sw.Stop();

      Console.WriteLine($"  🏆 Debate completed in {sw.Elapsed.TotalSeconds:F1}s");
      Console.WriteLine($"     Rounds:        {result.Rounds.Count}");
      Console.WriteLine();

      if (result.VotingTally is { } tally)
      {
         Console.WriteLine("  ── 🗳️ Voting Tally ──");
         Console.WriteLine($"  Method:        {tally.Method}");
         Console.WriteLine($"  Winning option: {tally.WinningOption} (score: {tally.Score:F2})");
         Console.WriteLine();
         Console.WriteLine("  | Option | Score |");
         Console.WriteLine("  |--------|-------|");
         foreach (var (option, score) in tally.Scores.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  | {option} | {score:F2} |");
      }
      else
      {
         Console.WriteLine("  (No voting tally produced — options may not have been extracted.)");
      }

      Console.WriteLine();
      Console.WriteLine("  💡 Built-in strategies:");
      Console.WriteLine("     • MajorityVotingStrategy    — top-ranked option gets 1 point each");
      Console.WriteLine("     • BordaCountVotingStrategy  — N-1 points for top, N-2 for second, …");
      Console.WriteLine("     • WeightedVotingStrategy    — per-member weights (e.g. SecurityExpert=2.0)");
      Console.WriteLine();
      Console.WriteLine("  💡 Implement IVotingStrategy for custom tallying (e.g. Condorcet, STV).");
   }
}
