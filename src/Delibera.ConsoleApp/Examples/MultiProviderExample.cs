using Delibera.Core.Council;
using Delibera.Core.Providers;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Multi-provider example — demonstrates mixing models from different LLM providers.
/// </summary>
public static class MultiProviderExample
{
   public static async Task RunAsync()
   {
      using var factory = new ProviderFactory();

      // Register custom providers:
      // factory.RegisterBuilder("YandexGPT", cfg => new YandexGPTProvider(cfg["Endpoint"], cfg["ApiKey"]));

      var ollamaCloud = factory.CreateOllama("https://api.ollama.com", "YOUR_KEY");
      // var ollamaLocal = factory.CreateOllama("http://localhost:11434");

      var executor = new CouncilBuilder()
         .AddMember("llama2", ollamaCloud, "Cloud Expert")
         .AddMember("qwen2.5", ollamaCloud, "Cloud Analyst")
         // .AddMember("mistral", ollamaLocal, "Local Expert")
         .SetChairman(Chairman.CreateStrict("qwen2.5", ollamaCloud))
         .WithCritiqueDebate()
         .WithSystemPrompt("You are a cybersecurity expert.")
         .WithUserPrompt("What are the top 3 security risks for cloud-native applications in 2025?")
         .WithMaxRounds(4)
         .SaveResultTo("./results/multi_provider_debate.md")
         .Build();

      Console.WriteLine(executor.GetInfo());

      var result = await executor.ExecuteAsync();
      Console.WriteLine($"\n🏆 Completed in {result.TotalDuration.TotalSeconds:F1}s");
      Console.WriteLine("📁 Saved to: ./results/multi_provider_debate.md");
   }
}