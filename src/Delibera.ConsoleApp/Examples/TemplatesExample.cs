using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Templates;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-07 Debate Templates &amp; Presets Library.
///    Runs the <see cref="DebateTemplate.ArchitectureReview"/> template against any
///    reachable Ollama endpoint and prints the council configuration before executing.
/// </summary>
public static class TemplatesExample
{
    /// <summary>Runs the templates demo.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  📋 Debate Templates (F-07)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  Pre-configured councils for common use cases.");
        Console.WriteLine();

        // ── List available templates ──
        Console.WriteLine("  Built-in templates:");
        Console.WriteLine("    • ArchitectureReview  — Architect, SecurityExpert, PerfEngineer (Critique)");
        Console.WriteLine("    • RiskAssessment      — Optimist, Pessimist, Realist, RiskManager (Consensus)");
        Console.WriteLine("    • CodeReview          — Reviewer, Defender, QA, TechLead (Critique)");
        Console.WriteLine("    • ProductDecision     — PM, TechLead, UXDesigner (Standard)");
        Console.WriteLine("    • SecurityAudit       — RedTeam, BlueTeam, Auditor (Critique, Strict Chairman)");
        Console.WriteLine("    • DataArchitecture    — DataEngineer, DBA, MLEngineer (Consensus)");
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
                Console.WriteLine("     Skipping live demo. Templates are wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see the preset councils in action.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;

        // ── Build & run an ArchitectureReview template ──
        Console.WriteLine("  ── Running ArchitectureReview template ──");
        var executor = DebateTemplate.ArchitectureReview
            .WithProvider(llm)
            .WithQuestion("Should a 5-person startup adopt microservices or a modular monolith?")
            .WithMaxRounds(1)
            .WithTemperature(0.5f)
            .WithTimeout(TimeSpan.FromMinutes(5))
            .SaveResultTo("./debate_results/templates_architecture.md")
            .Build();

        Console.WriteLine(executor.GetInfo());
        Console.WriteLine("  Starting debate…");

        executor.OnRoundCompleted += round =>
            Console.WriteLine($"  ✅ Round {round.RoundNumber} ({round.RoundName}) in {round.Duration.TotalSeconds:F1}s");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(ct);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"  🏆 Debate completed in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"     Rounds:        {result.Rounds.Count}");
        Console.WriteLine($"     Verdict chars: {result.FinalVerdict?.Length ?? 0}");
        Console.WriteLine();
        Console.WriteLine("  💡 Try other templates:");
        Console.WriteLine("     DebateTemplate.RiskAssessment.WithProvider(llm).WithQuestion(\"…\").Build()");
        Console.WriteLine("     DebateTemplate.CodeReview.WithProvider(llm).WithQuestion(\"…\").Build()");
        Console.WriteLine("     DebateTemplate.SecurityAudit.WithProvider(llm).WithQuestion(\"…\").Build()");
        Console.WriteLine("     DebateTemplate.Custom() — escape hatch to full CouncilBuilder");
    }
}