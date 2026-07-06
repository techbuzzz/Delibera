using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-01 Async Streaming Council feature:
///    <see cref="ICouncilExecutor.StreamDebateAsync(CancellationToken)"/> yields each
///    <see cref="DebateRound"/> as it completes so a CLI / SSE / WebSocket / Blazor
///    consumer can render live progress instead of waiting for the full debate.
/// </summary>
public static class StreamingCouncilExample
{
    /// <summary>Runs the streaming demo against any reachable Ollama endpoint.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  🌊 Streaming Council (F-01)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  IAsyncEnumerable<DebateRound> — rounds yielded live as they complete.");
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
                Console.WriteLine("     Skipping live demo. The streaming API is wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see rounds stream live.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;

        // ── Build a council whose debate we'll stream ──
        var executor = new CouncilBuilder()
            .AddMember("llama3.2", llm, "Analyst")
            .AddMember("qwen2.5", llm, "Critic")
            .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
            .WithStandardDebate()
            .WithSystemPrompt("You are thoughtful software architects.")
            .WithUserPrompt("Should a 5-person startup adopt microservices or a modular monolith?")
            .WithMaxRounds(2)
            .WithTemperature(0.5f)
            .Build();

        Console.WriteLine("  Starting debate (streaming)…");
        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var roundCount = 0;

        // ── Stream rounds live as they complete ──
        await foreach (var round in executor.StreamDebateAsync(ct))
        {
            roundCount++;
            var total = round.Total?.ToString() ?? "?";
            var finalTag = round.IsFinal ? " [FINAL]" : "";
            Console.WriteLine($"  ┌─ Round {round.RoundNumber}/{total}: {round.RoundName}{finalTag} ({round.Duration.TotalSeconds:F1}s)");
            Console.WriteLine($"  │  Participants: {round.Responses.Count}");
            foreach (var (member, response) in round.Responses)
            {
                var preview = response.Length > 120 ? response[..120] + "…" : response;
                Console.WriteLine($"  │  • {member}: {preview.Replace("\n", " ")}");
            }
            Console.WriteLine($"  └─");
            Console.WriteLine();
        }
        sw.Stop();

        Console.WriteLine($"  🏆 Streamed {roundCount} rounds in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"     LastStreamedResult rounds: {executor.LastStreamedResult?.Rounds.Count ?? 0}");
        Console.WriteLine($"     Final verdict chars:       {executor.LastStreamedResult?.FinalVerdict?.Length ?? 0}");
        Console.WriteLine();
        Console.WriteLine("  💡 Usage patterns:");
        Console.WriteLine("     // CLI live output");
        Console.WriteLine("     await foreach (var r in executor.StreamDebateAsync(ct))");
        Console.WriteLine("         Console.WriteLine($\"[Round {r.RoundNumber}/{r.Total}] {r.RoundName}\");");
        Console.WriteLine();
        Console.WriteLine("     // ASP.NET Core SSE");
        Console.WriteLine("     app.MapGet(\"/debate/stream\", async (ctx, executor) => {");
        Console.WriteLine("         ctx.Response.Headers.ContentType = \"text/event-stream\";");
        Console.WriteLine("         await foreach (var r in executor.StreamDebateAsync(ctx.RequestAborted)) {");
        Console.WriteLine("             await ctx.Response.WriteAsync($\"data: {JsonSerializer.Serialize(r)}\\n\\n\");");
        Console.WriteLine("             await ctx.Response.Body.FlushAsync();");
        Console.WriteLine("         }");
        Console.WriteLine("     });");
    }
}