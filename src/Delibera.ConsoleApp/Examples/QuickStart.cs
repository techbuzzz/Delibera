using Delibera.Core.Council;
using Delibera.Core.Knowledge;
using Delibera.Core.Providers;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
/// Minimal quick-start example — programmatic council setup without appsettings.json.
/// </summary>
public static class QuickStart
{
   public static async Task RunAsync()
   {
      // 1. Create providers
      using var factory = new ProviderFactory();
      var ollama = factory.CreateOllama("https://api.ollama.com", "YOUR_API_KEY");

      // 2. Load knowledge (optional)
      var kb = new MarkdownKnowledgeBase();
      // await kb.LoadAsync("./knowledge/context.md");

      // 3. Build council via fluent API
      var executor = new CouncilBuilder()
          .AddMember("llama2", ollama, "Analyst", "You are a data-driven analyst.")
          .AddMember("qwen2.5", ollama, "Strategist", "You are a strategic thinker.")
          .SetChairman(Chairman.CreateStandard("qwen2.5", ollama))
          .WithStandardDebate()
          .WithSystemPrompt("You are an expert in software engineering.")
          .WithUserPrompt("Should we use Kubernetes for a startup with 3 developers?")
          .WithMaxRounds(4)
          .WithTemperature(0.7f)
          // .WithKnowledge(kb)
          .SaveResultTo("./results/quick_debate.md")
          .Build();

      // 4. Subscribe to progress
      executor.OnRoundCompleted += round =>
          Console.WriteLine($"✅ Round {round.RoundNumber}: {round.RoundName} completed");

      // 5. Run
      var result = await executor.ExecuteAsync();

      Console.WriteLine($"\nFinal Verdict:\n{result.FinalVerdict}");
      Console.WriteLine($"\nSaved to: ./results/quick_debate.md");
   }
}
