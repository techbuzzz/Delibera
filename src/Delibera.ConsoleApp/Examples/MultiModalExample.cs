using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-06 Multi-Modal Council feature:
///    attachments (images, documents) are read via pluggable
///    <see cref="Delibera.Core.Attachments.IFileContentReader"/>s
///    and injected into the debate context.
/// </summary>
public static class MultiModalExample
{
    /// <summary>Runs the multi-modal demo against any reachable Ollama endpoint.</summary>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  🖼️ Multi-Modal Council (F-06)");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  Images, diagrams, and documents in the debate.");
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
                Console.WriteLine("     Skipping live demo. The multi-modal API is wired regardless —");
                Console.WriteLine("     run against any reachable Ollama to see attachments in action.");
                return;
            }
            ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
        }

        ILLMProvider llm = ollama;

        // ── Build a multi-modal council ──
        var executor = new CouncilBuilder()
            // Vision-capable member — explicit capability
            .AddMember("llava:13b", llm, "Visual Analyst",
                capabilities: MemberCapabilities.Vision | MemberCapabilities.Text,
                persona: "You are an expert in visual architecture diagrams.")
            // Text-only member — capability auto-detected from model name
            .AddMember("qwen2.5:7b", llm, "Strategist",
                persona: "You analyze requirements and propose architectural decisions.")
            // Attachments — single unified type, no PdfAttachment/ImageAttachment hierarchy
            .WithAttachment("./knowledge/architecture-overview.md", "Architecture overview document")
            // User-supplied reader for .pdf — library choice is yours (lambda style)
            .WithFileReader(".pdf", async (path, token) =>
            {
                // Placeholder: in a real app, call your PDF library here (PdfPig, iText, etc.)
                var text = $"[PDF content extracted from {Path.GetFileName(path)} by custom reader]";
                var meta = new Dictionary<string, string> { ["pages"] = "1" };
                return new Delibera.Core.Attachments.FileReadResult(path, text, null, meta);
            })
            .WithUserPrompt("Review this architecture against the stated requirements. Propose improvements.")
            .WithMaxRounds(1)
            .WithTemperature(0.5f)
            .SaveResultTo("./debate_results/multimodal_demo.md")
            .Build();

        Console.WriteLine($"  Attachments:     {executor.Attachments.Count}");
        Console.WriteLine($"  File readers:    {executor.FileReaders.GetType().Name}");
        Console.WriteLine($"  Vision members:  {executor.Members.Count(m => m.SupportsVision)}");
        Console.WriteLine();

        executor.OnRoundCompleted += round =>
            Console.WriteLine($"  ✅ Round {round.RoundNumber} completed ({round.Responses.Count} responses)");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(ct);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"  🏆 Debate completed in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"     Rounds:        {result.Rounds.Count}");
        Console.WriteLine($"     Verdict chars: {result.FinalVerdict?.Length ?? 0}");
        Console.WriteLine();
        Console.WriteLine("  💡 Usage:");
        Console.WriteLine("     builder.WithAttachment(\"./diagram.png\", \"System Architecture\")");
        Console.WriteLine("            .WithFileReader(\".pdf\", async (path, ct) => { ... })");
        Console.WriteLine("            .AddMember(\"llava:13b\", llm, \"Analyst\",");
        Console.WriteLine("                capabilities: MemberCapabilities.Vision | MemberCapabilities.Text)");
    }
}