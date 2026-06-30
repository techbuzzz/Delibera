using System.Diagnostics;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates cooperative cancellation across the entire Delibera pipeline:
///    Ctrl+C is captured into a <see cref="CancellationTokenSource" /> and the
///    CT is forwarded into <c>executor.ExecuteAsync(cts.Token)</c>, which
///    propagates it through rounds, LLM calls, MCP tools, RAG queries, and
///    even the final result-save step.
/// </summary>
public static class CancellationExample
{
   /// <summary>
   ///    Runs the cancellation example. Press Ctrl+C at any time to abort.
   /// </summary>
   public static async Task RunAsync()
   {
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  🛑 Cancellation Example");
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  Press Ctrl+C at any time to cancel.");
      Console.WriteLine("  The debate is wired to a CancellationTokenSource that");
      Console.WriteLine("  aborts mid-flight (rounds, LLM calls, file saves).");
      Console.WriteLine();

      using var cts = new CancellationTokenSource();

      // Capture Ctrl+C (and Ctrl+Break on Windows). Cancel the source AND
      // suppress the default SIGINT-terminates-process behaviour so we can
      // surface a friendly diagnostic instead.
      Console.CancelKeyPress += (_, e) =>
      {
         e.Cancel = true;
         if (!cts.IsCancellationRequested)
         {
            Console.WriteLine("\n⚠️  Ctrl+C detected — signaling cancellation...");
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* race */ }
         }
      };

      // 1. Create a provider (Ollama local is the easiest for a demo).
      using var factory = new ProviderFactory();
      OllamaProvider? ollama = null;
      try
      {
         ollama = factory.CreateLocalOllama("http://localhost:11434");
         if (!await ollama.IsAvailableAsync(cts.Token))
         {
            Console.WriteLine("  ⚠️  Local Ollama not available — falling back to Ollama Cloud (set OLLAMA_API_KEY env).");
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
            Console.WriteLine("     Skipping live demo. The cancellation plumbing still works —");
            Console.WriteLine("     just run the example against any reachable Ollama endpoint.");
            return;
         }
         ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
      }

      ILLMProvider llm = ollama; // widen once for the council builder

      // 2. Build a small, fast council (1 round, 2 members) so cancellation has a chance to be exercised.
      var executor = new CouncilBuilder()
         .AddMember("llama2", llm, "Analyst")
         .AddMember("qwen2.5", llm, "Strategist")
         .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
         .WithStandardDebate()
         .WithSystemPrompt("You are a thoughtful expert in software engineering.")
         .WithUserPrompt("Compare microservices vs a modular monolith for a 10-person startup.")
         .WithMaxRounds(1)
         .WithTemperature(0.5f)
         .SaveResultTo("./debate_results/cancellation_demo.md")
         .Build();

      executor.OnRoundCompleted += round =>
         Console.WriteLine($"  ✅ Round {round.RoundNumber} completed in {round.Duration.TotalSeconds:F1}s");
      executor.OnLog += e => Console.WriteLine($"  ┊ {e}");

      // 3. Heartbeat — proves the main thread is alive even while the council is awaiting.
      //    This task is itself cancellable; it exits cleanly when the user hits Ctrl+C.
      using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
      var heartbeat = Task.Run(async () =>
      {
         var sw = Stopwatch.StartNew();
         try
         {
            while (!heartbeatCts.Token.IsCancellationRequested)
            {
               Console.WriteLine($"  💓 Still working... ({sw.Elapsed:mm\\:ss}) — press Ctrl+C to cancel");
               await Task.Delay(TimeSpan.FromSeconds(2), heartbeatCts.Token);
            }
         }
         catch (OperationCanceledException) { /* expected on shutdown */ }
      });

      // 4. Run the council with the token.
      var swTotal = Stopwatch.StartNew();
      try
      {
         var result = await executor.ExecuteAsync(cts.Token);
         swTotal.Stop();

         Console.WriteLine($"\n🏆 Debate completed in {swTotal.Elapsed:mm\\:ss}");
         Console.WriteLine($"   Rounds:        {result.Rounds.Count}");
         Console.WriteLine($"   Verdict chars: {result.FinalVerdict?.Length ?? 0}");

         // Also demonstrate that the save methods honor cancellation.
         var outputDir = "./debate_results/cancellation_demo";
         Directory.CreateDirectory(outputDir);
         var (r, s, l) = await result.SaveAllAsync(outputDir, ct: cts.Token);
         Console.WriteLine($"   Result:        {r}");
         Console.WriteLine($"   Statistics:    {s}");
         Console.WriteLine($"   Logs:          {l}");
      }
      catch (OperationCanceledException) when (cts.IsCancellationRequested)
      {
         swTotal.Stop();
         Console.WriteLine($"\n🛑 Debate cancelled after {swTotal.Elapsed:mm\\:ss}.");
         Console.WriteLine("   No partial result was saved (cancellation propagated through all layers).");
      }
      finally
      {
         heartbeatCts.Cancel();
         try { await heartbeat; } catch { /* ignore */ }
      }
   }
}
