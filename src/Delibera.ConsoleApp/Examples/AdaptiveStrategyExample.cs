using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-09 Dynamic Strategy Switching feature:
///    <see cref="ICouncilBuilder.WithAdaptiveStrategy(IStrategySelector)"/> lets the
///    council swap debate strategy mid-flight when stagnation is detected.
/// </summary>
public static class AdaptiveStrategyExample
{
    /// <summary>Runs the adaptive-strategy demo against any reachable Ollama endpoint.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  🔄 Adaptive Strategy Switching (F-09)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  Council swaps strategy mid-flight when responses stagnate.");
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
                Console.WriteLine("     Skipping live demo. The adaptive strategy API is wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see the switch in action.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;

        // ── Build a council with adaptive strategy switching ──
        var selector = new AdaptiveStrategySelector
        {
            Initial = new StandardDebate(),
            OnStalemate = new CritiqueDebate(),
            StagnationThreshold = 2,
            StagnationScore = 0.3
        };

        var executor = new CouncilBuilder()
            .AddMember("llama3.2", llm, "Analyst")
            .AddMember("qwen2.5", llm, "Critic")
            .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
            .WithUserPrompt("Should a 5-person startup adopt microservices or a modular monolith?")
            .WithMaxRounds(3)
            .WithTemperature(0.5f)
            .WithAdaptiveStrategy(selector)
            .Build();

        Console.WriteLine($"  Initial strategy:    {executor.Strategy.StrategyName}");
        Console.WriteLine($"  On-stalemate switch: {selector.OnStalemate.StrategyName}");
        Console.WriteLine($"  Stagnation threshold: {selector.StagnationThreshold} consecutive rounds");
        Console.WriteLine($"  Stagnation score:     < {selector.StagnationScore}");
        Console.WriteLine();

        // Track which strategy produced each round
        executor.OnRoundCompleted += round =>
        {
            var stratName = round.StrategyUsed?.StrategyName ?? "(unknown)";
            Console.WriteLine($"  ✅ Round {round.RoundNumber} ({round.RoundName}) — strategy: {stratName} ({round.Duration.TotalSeconds:F1}s)");
        };

        Console.WriteLine("  Starting debate…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(ct);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"  🏆 Debate completed in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"     Rounds:        {result.Rounds.Count}");
        Console.WriteLine($"     StrategyName:  {result.StrategyName}");
        Console.WriteLine();

        // Show the strategy-switch log entries
        var switchLogs = result.ExecutionLogs.Where(l => l.Message.Contains("Adaptive strategy switch")).ToList();
        if (switchLogs.Count > 0)
        {
            Console.WriteLine("  ── Strategy switch log ──");
            foreach (var log in switchLogs)
                Console.WriteLine($"    {log.Timestamp:HH:mm:ss} {log.Message}");
        }
        else
        {
            Console.WriteLine("  No strategy switch was triggered (responses stayed diverse).");
        }

        Console.WriteLine();
        Console.WriteLine("  💡 Usage:");
        Console.WriteLine("     var selector = new AdaptiveStrategySelector {");
        Console.WriteLine("         Initial = new StandardDebate(),");
        Console.WriteLine("         OnStalemate = new CritiqueDebate(),");
        Console.WriteLine("         StagnationThreshold = 2");
        Console.WriteLine("     };");
        Console.WriteLine("     builder.WithAdaptiveStrategy(selector);");
    }
}