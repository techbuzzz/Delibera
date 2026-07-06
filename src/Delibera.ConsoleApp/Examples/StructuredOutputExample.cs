using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-05 Structured Output / JSON Schema feature:
///    <see cref="ICouncilBuilder.WithStructuredOutput{TVerdict}"/> configures the
///    Chairman to emit a typed JSON verdict conforming to a generated schema.
/// </summary>
public static class StructuredOutputExample
{
    /// <summary>The architecture decision type produced by the debate.</summary>
    public sealed record ArchitectureDecision(
        string Recommendation,
        double Confidence,
        IReadOnlyList<string> Risks,
        IReadOnlyList<string> Benefits,
        string Rationale);

    /// <summary>Runs the structured-output demo against any reachable Ollama endpoint.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  📋 Structured Output (F-05)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  JSON-schema-validated verdicts from the Chairman.");
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
                Console.WriteLine("     Skipping live demo. The structured output API is wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see typed verdicts.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;

        // ── Build a council with structured output ──
        var executor = new CouncilBuilder()
            .AddMember("llama3.2", llm, "Architect")
            .AddMember("qwen2.5", llm, "SecurityExpert")
            .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
            .WithStandardDebate()
            .WithUserPrompt("Should we migrate our monolith to microservices? Decide now.")
            .WithMaxRounds(1)
            .WithTemperature(0.5f)
            .WithStructuredOutput<ArchitectureDecision>()
            .SaveResultTo("./debate_results/structured_output_demo.md")
            .Build();

        Console.WriteLine($"  Structured output: {executor.StructuredOutputType?.Name ?? "(none)"}");
        Console.WriteLine();

        Console.WriteLine("  Starting debate…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>(ct);
        sw.Stop();

        Console.WriteLine($"  🏆 Debate completed in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine();

        if (verdict is not null)
        {
            Console.WriteLine("  ── Typed Verdict ──");
            Console.WriteLine($"  Recommendation: {verdict.Recommendation}");
            Console.WriteLine($"  Confidence:     {verdict.Confidence:P0}");
            Console.WriteLine($"  Benefits:       {(verdict.Benefits is { } b ? string.Join(", ", b) : "(none)")}");
            Console.WriteLine($"  Risks:          {(verdict.Risks is { } r ? string.Join(", ", r) : "(none)")}");
            Console.WriteLine($"  Rationale:      {verdict.Rationale}");
        }
        else
        {
            Console.WriteLine("  ❌ Failed to deserialise a structured verdict.");
            Console.WriteLine($"  Raw FinalVerdict: {result.FinalVerdict?[..Math.Min(200, result.FinalVerdict?.Length ?? 0)]}…");
        }

        Console.WriteLine();
        Console.WriteLine("  💡 Usage:");
        Console.WriteLine("     builder.WithStructuredOutput<ArchitectureDecision>();");
        Console.WriteLine("     var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();");
    }
}