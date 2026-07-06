using Delibera.Core.Council;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Interfaces;
using Delibera.Core.Persistence;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-03 Debate Persistence &amp; Resume feature:
///    a checkpoint is saved after every round so the debate can be resumed
///    after a crash or intentional pause.
/// </summary>
public static class PersistenceExample
{
    /// <summary>Runs the persistence demo against any reachable Ollama endpoint.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  💾 Debate Persistence & Resume (F-03)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  Checkpoint after every round → resume from last completed round.");
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
                Console.WriteLine("     Skipping live demo. The persistence API is wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see checkpoints in action.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;

        // ── File-backed checkpoint store ──
        var checkpointDir = "./checkpoints";
        var store = new FileDebateStore(checkpointDir, retentionDays: 30);

        // ── Phase 1: Run a fresh debate (or resume if a checkpoint exists) ──
        var existing = (await store.ListAsync(ct))
            .FirstOrDefault(m => m.OriginalQuestion.StartsWith("Should a 5-person", StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            Console.WriteLine($"  ♻️  Resuming existing debate: {existing.DebateId}");
            Console.WriteLine($"     Last completed round: {existing.LastCompletedRound}");
            Console.WriteLine();
        }

        var builder = new CouncilBuilder()
            .AddMember("llama3.2", llm, "Architect")
            .AddMember("qwen2.5", llm, "Critic")
            .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
            .WithStandardDebate()
            .WithUserPrompt("Should a 5-person startup adopt microservices or a modular monolith?")
            .WithMaxRounds(2)
            .WithTemperature(0.5f)
            .WithPersistence(store);

        if (existing is not null)
            builder.ResumeFrom(existing.DebateId);

        var executor = builder.Build();
        Console.WriteLine($"  DebateStore:    {executor.DebateStore?.GetType().Name ?? "(none)"}");
        Console.WriteLine($"  ResumeFrom:     {executor.ResumeFromDebateId ?? "(fresh)"}");
        Console.WriteLine();

        executor.OnRoundCompleted += round =>
            Console.WriteLine($"  ✅ Round {round.RoundNumber} completed → checkpoint saved");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(ct);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"  🏆 Debate completed in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"     Rounds:        {result.Rounds.Count}");
        Console.WriteLine();

        // ── Phase 2: List stored checkpoints ──
        Console.WriteLine("  ── Stored checkpoints ──");
        var list = await store.ListAsync(ct);
        foreach (var meta in list.Take(5))
        {
            Console.WriteLine($"    • {meta.DebateId}  round {meta.LastCompletedRound}  \"{Truncate(meta.OriginalQuestion, 60)}\"");
        }

        Console.WriteLine();
        Console.WriteLine("  💡 Usage:");
        Console.WriteLine("     var store = new FileDebateStore(\"./checkpoints\");");
        Console.WriteLine("     builder.WithPersistence(store).ResumeFrom(\"debate-id-123\");");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max] + "…";
}