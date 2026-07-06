using Delibera.Core.Benchmarking;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Output;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-10 Quick Wins bundle:
///    <list type="bullet">
///       <item>F-10a <see cref="DebateResult.ToHtml(HtmlExportOptions?)"/> HTML export</item>
///       <item>F-10b <see cref="ICouncilBuilder.WithTimeout(TimeSpan)"/> debate-level timeout</item>
///       <item>F-10c <see cref="Persona"/> presets (DevilsAdvocate, RiskManager, ...)</item>
///       <item>F-10d <see cref="CouncilBenchmark"/> side-by-side model comparison</item>
///       <item>F-10e <see cref="ICouncilBuilder.WithParticipantLimit(int)"/> safety guard</item>
///    </list>
/// </summary>
public static class QuickWinsExample
{
    /// <summary>Runs the quick-wins demo against any reachable Ollama endpoint.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  ⚡ Quick Wins Bundle (F-10)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  HTML export · Timeout · Personas · Benchmark · ParticipantLimit");
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
                Console.WriteLine("     Skipping live demo. The Quick Wins APIs are wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see HTML/Persona/Benchmark in action.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;
        Directory.CreateDirectory("./debate_results/quickwins");

        // ══════════ F-10c: Persona presets ══════════
        Console.WriteLine("  ── F-10c: Persona presets ──");
        Console.WriteLine("  Building a council with three built-in personas:");
        Console.WriteLine("    • Devil's Advocate (challenges consensus)");
        Console.WriteLine("    • Data-Driven Analyst (demands numbers)");
        Console.WriteLine("    • Risk Manager (enumerates risks)");
        Console.WriteLine();

        var personaExecutor = new CouncilBuilder()
            .AddMember("llama3.2", llm, "Devil's Advocate", Persona.DevilsAdvocate)
            .AddMember("qwen2.5", llm, "Data Analyst", Persona.DataDrivenAnalyst)
            .AddMember("mistral", llm, "Risk Manager", Persona.RiskManager)
            .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
            .WithStandardDebate()
            .WithSystemPrompt("You are software architects debating an important decision.")
            .WithUserPrompt("Should we adopt a microservices architecture for a 5-person startup?")
            .WithMaxRounds(1)
            .WithTemperature(0.5f)
            .WithTimeout(TimeSpan.FromMinutes(5)) // F-10b
            .WithParticipantLimit(5)              // F-10e
            .SaveResultTo("./debate_results/quickwins/personas_result.md")
            .Build();

        Console.WriteLine($"  DebateTimeout:       {personaExecutor.DebateTimeout}");
        Console.WriteLine($"  IsTelemetryEnabled:  {personaExecutor.IsTelemetryEnabled}");
        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await personaExecutor.ExecuteAsync(ct);
        sw.Stop();
        Console.WriteLine($"  🏆 Persona debate completed in {sw.Elapsed.TotalSeconds:F1}s ({result.Rounds.Count} rounds)");

        // ══════════ F-10a: HTML export ══════════
        Console.WriteLine();
        Console.WriteLine("  ── F-10a: HTML export ──");
        var htmlPath = "./debate_results/quickwins/result.html";
        await result.SaveToHtmlAsync(htmlPath, new HtmlExportOptions { Theme = HtmlTheme.Dark, CollapsibleRounds = true }, ct);
        Console.WriteLine($"  Saved self-contained HTML: {htmlPath}");
        var htmlSize = new FileInfo(htmlPath).Length;
        Console.WriteLine($"  File size: {htmlSize / 1024.0:F1} KB (inline CSS, collapsible <details>)");

        // ══════════ F-10d: Benchmark / model comparison ══════════
        Console.WriteLine();
        Console.WriteLine("  ── F-10d: Benchmark / model comparison ──");
        Console.WriteLine("  Running the same question across two configurations…");
        Console.WriteLine();

        var benchmark = new CouncilBenchmark()
            .AddConfiguration("Light", b => b
                .AddMember("llama3.2", llm, "Analyst")
                .AddMember("qwen2.5", llm, "Critic")
                .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
                .WithStandardDebate())
            .AddConfiguration("Heavy", b => b
                .AddMember("mistral", llm, "Architect")
                .AddMember("llama3.2", llm, "Devil's Advocate", Persona.DevilsAdvocate)
                .SetChairman(Chairman.CreateStandard("mistral", llm))
                .WithCritiqueDebate())
            .WithQuestion("Microservices vs modular monolith for a 5-person startup?")
            .WithMaxRounds(1);

        var report = await benchmark.RunAsync(ct);
        var benchPath = "./debate_results/quickwins/benchmark.md";
        await report.SaveComparisonAsync(benchPath, ct);
        Console.WriteLine($"  Saved comparison: {benchPath}");
        Console.WriteLine($"  Configurations compared: {report.Entries.Count}");
        foreach (var e in report.Entries)
        {
            if (e.Error is not null)
                Console.WriteLine($"    ❌ {e.Name}: {e.Error}");
            else
                Console.WriteLine($"    ✅ {e.Name}: {e.Result!.Rounds.Count} rounds, {e.Result.TotalDuration.TotalSeconds:F1}s, verdict {e.Result.FinalVerdict?.Length ?? 0} chars");
        }

        Console.WriteLine();
        Console.WriteLine("  ✨ Quick Wins demo complete. Files in ./debate_results/quickwins/");
    }
}