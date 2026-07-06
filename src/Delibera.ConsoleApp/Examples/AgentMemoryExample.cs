using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Memory;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-04 Agent Memory &amp; Long-Term Context feature:
///    council members recall context from previous sessions and persist their
///    conclusions after each debate.
/// </summary>
public static class AgentMemoryExample
{
    /// <summary>Runs the agent-memory demo against any reachable Ollama endpoint.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  🧠 Agent Memory (F-04)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  Council members recall + persist across sessions.");
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
                Console.WriteLine("     Skipping live demo. The agent memory API is wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see recall/persist in action.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;

        // ── Build a council with agent memory (in-memory by default) ──
        var memory = new InMemoryAgentMemory();

        var executor = new CouncilBuilder()
            .AddMember("llama3.2", llm, "Architect")
            .AddMember("qwen2.5", llm, "Pragmatist")
            .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
            .WithStandardDebate()
            .WithUserPrompt("What architecture should we adopt for our new microservice?")
            .WithMaxRounds(1)
            .WithTemperature(0.5f)
            .WithAgentMemory(memory)
            .Build();

        Console.WriteLine($"  Memory backend:  {executor.AgentMemory?.GetType().Name ?? "(none)"}");
        Console.WriteLine();

        Console.WriteLine("  ── Debate 1: fresh start ──");
        var result1 = await executor.ExecuteAsync(ct);
        Console.WriteLine($"     Rounds:        {result1.Rounds.Count}");
        Console.WriteLine($"     Verdict chars: {result1.FinalVerdict?.Length ?? 0}");
        Console.WriteLine();

        Console.WriteLine("  ── Storing memories from Debate 1 ──");
        var architectRecall = await memory.RecallAsync("Architect: llama3.2 (Ollama)", "microservice architecture");
        Console.WriteLine($"     Architect memories recalled: {architectRecall.Count}");
        foreach (var m in architectRecall)
            Console.WriteLine($"       • {Truncate(m.Content, 80)}");

        var chairmanRecall = await memory.RecallAsync("Chairman", "microservice");
        Console.WriteLine($"     Chairman memories recalled:  {chairmanRecall.Count}");
        Console.WriteLine();

        Console.WriteLine("  💡 Usage:");
        Console.WriteLine("     builder.WithAgentMemory(new InMemoryAgentMemory());");
        Console.WriteLine("     // or: new QdrantAgentMemory(rag, embeddings)");
        Console.WriteLine("     // or: new PgVectorAgentMemory(rag, embeddings);");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max] + "…";
}