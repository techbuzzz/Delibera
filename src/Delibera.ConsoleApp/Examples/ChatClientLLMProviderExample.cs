using Delibera.Core.Extensions;
using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.AI;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the <see cref="ChatClientLLMProvider" /> universal provider and the
///    <see cref="MicrosoftAIExtensions" /> interop bridge that lets Delibera talk to any
///    Microsoft.Extensions.AI <see cref="IChatClient" /> / <see cref="IEmbeddingGenerator{TInput,TEmbedding}" />
///    (OpenAI, Azure OpenAI, Ollama, Anthropic, LM Studio, vLLM, …) and vice-versa.
/// </summary>
/// <remarks>
///    Run with: <c>dotnet run --project src/Delibera.ConsoleApp -- --chatclient</c>
/// </remarks>
public static class ChatClientLLMProviderExample
{
   public static async Task RunAsync()
   {
      Console.WriteLine("🔌 ChatClientLLMProvider — Microsoft.Extensions.AI integration\n");

      // ──────────────────────────────────────────────────────────────
      // 1. Obtain an IChatClient from any backend
      // ──────────────────────────────────────────────────────────────
      // The repo references OllamaSharp (which natively implements IChatClient),
      // so we use a local Ollama server here. The SAME pattern works for any
      // Microsoft.Extensions.AI backend, e.g. OpenAI:
      //
      //    using OpenAI;
      //    var openAiClient = new OpenAIClient(apiKey);
      //    IChatClient chatClient = openAiClient.GetChatClient("gpt-4").AsIChatClient();
      //
      const string endpoint = "http://localhost:11434"; // local Ollama
      const string model = "gpt-oss";

      using var ollama = new OllamaProvider(endpoint);
      IChatClient chatClient = ollama.AsChatClient();
      Console.WriteLine($"  ✦ Backend IChatClient: {ollama.ProviderName}");

      // ──────────────────────────────────────────────────────────────
      // 2. Compose the Microsoft.Extensions.AI middleware pipeline
      // ──────────────────────────────────────────────────────────────
      // WithMiddleware adds function-invocation + logging around the inner client.
      // (Pass an ILoggerFactory to enable console logging.)
      var decoratedClient = chatClient.WithMiddleware(enableFunctionInvocation: true);
      Console.WriteLine("  ✦ Middleware: function invocation enabled");

      // ──────────────────────────────────────────────────────────────
      // 3. Adopt the IChatClient as a Delibera ILLMProvider
      // ──────────────────────────────────────────────────────────────
      // Two equivalent forms:
      //    var llmProvider = new ChatClientLLMProvider(decoratedClient);
      //    var llmProvider = decoratedClient.AsLLMProvider();
      var llmProvider = new ChatClientLLMProvider(decoratedClient, "Ollama (via ChatClientLLMProvider)");
      Console.WriteLine($"  ✦ ILLMProvider: {llmProvider.ProviderName}");
      Console.WriteLine($"    DefaultModelId: {llmProvider.DefaultModelId ?? "(none)"}");

      // ──────────────────────────────────────────────────────────────
      // 4. Provider introspection
      // ──────────────────────────────────────────────────────────────
      Console.WriteLine("\n  🩺 Provider introspection:");
      try
      {
         var available = await llmProvider.IsAvailableAsync();
         Console.WriteLine($"    IsAvailableAsync:  {available}");

         var models = await llmProvider.ListModelsAsync();
         Console.WriteLine($"    ListModelsAsync:    {(models.Count > 0 ? string.Join(", ", models) : "(empty — M.E.AI has no enumeration contract)")}");

         var caps = await llmProvider.GetModelCapabilitiesAsync(model);
         Console.WriteLine(caps is not null
            ? $"    GetModelCapabilitiesAsync('{model}'): context window = {caps.ContextWindowTokens} tokens"
            : $"    GetModelCapabilitiesAsync('{model}'): null (falls back to static registry)");
      }
      catch (Exception ex)
      {
         Console.WriteLine($"    ⚠️  {ex.Message}");
      }

      // ──────────────────────────────────────────────────────────────
      // 5. One-shot chat
      // ──────────────────────────────────────────────────────────────
      Console.WriteLine("\n  💬 ChatAsync:");
      try
      {
         var response = await llmProvider.ChatAsync(
            model: model,
            systemPrompt: "You are a helpful assistant.",
            userPrompt: "What is the capital of France?",
            temperature: 0.7f);
         Console.WriteLine($"    {response}");
      }
      catch (Exception ex)
      {
         PrintTips(ex);
         return;
      }

      // ──────────────────────────────────────────────────────────────
      // 6. Streaming chat
      // ──────────────────────────────────────────────────────────────
      Console.WriteLine("\n  🌊 ChatStreamAsync (token-by-token):\n    ");
      try
      {
         await foreach (var chunk in llmProvider.ChatStreamAsync(
                           model,
                           "You are a concise assistant.",
                           "In one sentence, what is Microsoft.Extensions.AI?"))
            Console.Write(chunk);
         Console.WriteLine();
      }
      catch (Exception ex)
      {
         Console.WriteLine($"    ⚠️  {ex.Message}");
      }

      // ──────────────────────────────────────────────────────────────
      // 7. Reverse bridge: expose ILLMProvider as IChatClient
      // ──────────────────────────────────────────────────────────────
      // AsChatClient() returns the underlying IChatClient when the provider
      // is already a ChatClientLLMProvider (no extra wrapping layer).
      var roundTripped = llmProvider.AsChatClient();
      Console.WriteLine($"\n  ↩️  AsChatClient() round-trip: {roundTripped.GetType().Name}");

      // ──────────────────────────────────────────────────────────────
      // 8. Embeddings via IEmbeddingGenerator -> IEmbeddingProvider
      // ──────────────────────────────────────────────────────────────
      Console.WriteLine("\n  🧮 Embeddings (IEmbeddingGenerator -> IEmbeddingProvider):");
      try
      {
         var embeddingProvider = ollama
            .AsEmbeddingGenerator()
            .AsEmbeddingProvider("nomic-embed-text");

         var vector = await embeddingProvider.EmbedAsync("Delibera deliberates.");
         Console.WriteLine($"    Model:    {embeddingProvider.EmbeddingModelName}");
         Console.WriteLine($"    Dimensions: {vector.Length}");
      }
      catch (Exception ex)
      {
         Console.WriteLine($"    ⚠️  {ex.Message} (embedding model may not be pulled)");
      }

      llmProvider.Dispose();
      Console.WriteLine("\n✅ Done. See the OpenAI variant in the comments at the top of this file.");
   }

   private static void PrintTips(Exception ex)
   {
      Console.WriteLine($"\n❌ {ex.Message}");
      Console.WriteLine("\n💡 Tips:");
      Console.WriteLine("   • Start a local Ollama server: ollama serve");
      Console.WriteLine("   • Pull the model: ollama pull llama3.2");
      Console.WriteLine("   • Pull an embedding model: ollama pull nomic-embed-text");
      Console.WriteLine("   • To use OpenAI instead, add the Microsoft.Extensions.AI.OpenAI package:");
      Console.WriteLine("       var openAiClient = new OpenAIClient(apiKey);");
      Console.WriteLine("       IChatClient chatClient = openAiClient.GetChatClient(\"gpt-4\").AsIChatClient();");
      Console.WriteLine("       var llmProvider = new ChatClientLLMProvider(chatClient);");
   }
}
