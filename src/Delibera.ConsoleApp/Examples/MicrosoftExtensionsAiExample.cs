using Delibera.Core.Council;
using Delibera.Core.Extensions;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.AI;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the Microsoft.Extensions.AI integration: using the standard
///    <see cref="IChatClient" /> / <see cref="IEmbeddingGenerator{TInput,TEmbedding}" /> abstractions
///    inside Delibera, composing middleware, and bridging providers in both directions.
/// </summary>
/// <remarks>
///    Run with: <c>dotnet run --project src/Delibera.ConsoleApp -- --msai</c>
/// </remarks>
public static class MicrosoftExtensionsAiExample
{
   public static async Task RunAsync()
   {
      Console.WriteLine("🤝 Microsoft.Extensions.AI integration demo\n");

      // ──────────────────────────────────────────────────────────────
      // A. Use ANY IChatClient as a Delibera ILLMProvider
      // ──────────────────────────────────────────────────────────────
      // OllamaSharp's OllamaApiClient natively implements IChatClient, so we use it here.
      // The same pattern applies to OpenAI / Azure OpenAI clients from Microsoft.Extensions.AI.OpenAI:
      //
      //   IChatClient openAi = new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey).AsIChatClient();
      //   ILLMProvider provider = openAi.AsLLMProvider("OpenAI");
      //
      const string endpoint = "http://localhost:11434"; // local Ollama (agent VM)
      const string model = "llama3.2";

      using var ollama = new OllamaProvider(endpoint);

      // Grab the underlying IChatClient and wrap it with the standard middleware pipeline
      // (function invocation + logging). This is the heart of "maximising the package potential".
      var pipeline = ollama
         .AsChatClient()
         .WithMiddleware(true);

      // Expose the decorated client back to Delibera as a normal provider.
      var msaiProvider = pipeline.AsLLMProvider("Ollama (via Microsoft.Extensions.AI)");
      Console.WriteLine($"  ✦ Provider name: {msaiProvider.ProviderName}");

      // ──────────────────────────────────────────────────────────────
      // B. Streaming via the unified abstraction
      // ──────────────────────────────────────────────────────────────
      Console.WriteLine("\n  💬 Streaming response (token-by-token):\n");
      try
      {
         await foreach (var chunk in msaiProvider.ChatStreamAsync(
                           model,
                           "You are a concise assistant.",
                           "In one sentence, what is Microsoft.Extensions.AI?"))
            Console.Write(chunk);
         Console.WriteLine();
      }
      catch (Exception ex)
      {
         PrintTips(ex);
         return;
      }

      // ──────────────────────────────────────────────────────────────
      // C. Embeddings through IEmbeddingGenerator
      // ──────────────────────────────────────────────────────────────
      Console.WriteLine("\n  🧮 Embedding via IEmbeddingGenerator:");
      try
      {
         var embeddings = ollama
            .AsEmbeddingGenerator()
            .AsEmbeddingProvider("nomic-embed-text");

         var vector = await embeddings.EmbedAsync("Delibera deliberates.");
         Console.WriteLine($"    dimensions = {vector.Length}");
      }
      catch (Exception ex)
      {
         Console.WriteLine($"    ⚠️  {ex.Message} (embedding model may not be pulled)");
      }

      // ──────────────────────────────────────────────────────────────
      // D. Plug the standard client into a full council via the factory
      // ──────────────────────────────────────────────────────────────
      Console.WriteLine("\n  🏛️  Building a council from an IChatClient...");
      using var factory = new ProviderFactory();
      var councilProvider = factory.CreateFromChatClient(
         "msai",
         ollama.AsChatClient(),
         "Ollama");

      var executor = new CouncilBuilder()
         .AddMember(model, councilProvider, "Architect")
         .AddMember(model, councilProvider, "Skeptic")
         .SetChairman(Chairman.CreateStandard(model, councilProvider))
         .WithStandardDebate()
         .WithSystemPrompt("You are a software architecture expert.")
         .WithUserPrompt("Is Microsoft.Extensions.AI a good abstraction for multi-provider apps?")
         .WithMaxRounds(2)
         .SaveResultTo("./results/msai_debate.md")
         .Build();

      Console.WriteLine(executor.GetInfo());

      try
      {
         var result = await executor.ExecuteAsync();
         Console.WriteLine($"\n🏆 Completed in {result.TotalDuration.TotalSeconds:F1}s");
         Console.WriteLine("📁 Saved to: ./results/msai_debate.md");
      }
      catch (Exception ex)
      {
         PrintTips(ex);
      }
   }

   private static void PrintTips(Exception ex)
   {
      Console.WriteLine($"\n❌ {ex.Message}");
      Console.WriteLine("\n💡 Tips:");
      Console.WriteLine("   • Start a local Ollama server: ollama serve");
      Console.WriteLine("   • Pull the model: ollama pull llama3.2");
      Console.WriteLine("   • Pull an embedding model: ollama pull nomic-embed-text");
      Console.WriteLine("   • To use OpenAI instead, add the Microsoft.Extensions.AI.OpenAI package");
      Console.WriteLine("     and build an IChatClient from your OpenAI key, then call .AsLLMProvider().");
   }
}