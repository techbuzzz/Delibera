using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
/// RAG example — demonstrates Qdrant integration, document indexing,
/// Knowledge Keeper and a debate enriched with vector-search context.
/// </summary>
public static class RagExample
{
   public static async Task RunAsync()
   {
      // ── 1. LLM provider ──
      using var factory = new ProviderFactory();
      var ollama = factory.CreateOllama("http://localhost:11434");

      // ── 2. Embedding provider (uses Ollama) ──
      var embeddings = new OllamaEmbeddingProvider(ollama, "nomic-embed-text");

      // ── 3. RAG provider (Qdrant) ──
      await using var ragFactory = new RagProviderFactory();
      var rag = ragFactory.CreateQdrant(embeddings, "localhost", 6334);

      // ── 4. Knowledge Keeper ──
      var kkModel = new CouncilMember("llama2", ollama, "Knowledge Keeper");
      var keeper = new KnowledgeKeeper(rag, kkModel, "architecture_kb");

      // ── 5. Index documents ──
      Console.WriteLine("📚 Indexing documents...");

      var docText = """
            # Microservices Best Practices
            
            ## Service Design
            - Each microservice should own its data
            - Services communicate via APIs (REST, gRPC, or message queues)
            - Keep services small and focused (single responsibility)
            
            ## Deployment
            - Use containers (Docker) for consistent environments
            - Kubernetes for orchestration at scale
            - CI/CD pipelines per service
            
            ## Monitoring
            - Distributed tracing (Jaeger, Zipkin)
            - Centralised logging (ELK stack)
            - Health checks and circuit breakers
            
            ## When NOT to use microservices
            - Small teams (< 5 developers)
            - Early-stage startups exploring product-market fit
            - Simple CRUD applications
            - When the domain is not well understood
            """;

      var chunks = await keeper.IndexDocumentAsync(docText, new() { ["topic"] = "microservices" });
      Console.WriteLine($"  ✅ Indexed {chunks} chunks");

      // ── 6. Build council with Knowledge Keeper ──
      var executor = new CouncilBuilder()
          .AddMember("llama2", ollama, "Backend Expert")
          .AddMember("qwen2.5", ollama, "DevOps Expert")
          .SetChairman(Chairman.CreateStandard("qwen2.5", ollama))
          .WithKnowledgeKeeper(keeper)
          .WithStandardDebate()
          .WithSystemPrompt("You are a senior software architect.")
          .WithUserPrompt("Our startup has 4 developers. Should we adopt microservices now or start with a monolith?")
          .WithMaxRounds(4)
          .SaveResultTo("./results/rag_debate.md")
          .Build();

      Console.WriteLine(executor.GetInfo());

      // ── 7. Run debate ──
      executor.OnRoundCompleted += round =>
      {
         Console.WriteLine($"✅ Round {round.RoundNumber}: {round.RoundName}");
         if (round.KnowledgeInteractions.Count > 0)
            Console.WriteLine($"   📚 {round.KnowledgeInteractions.Count} Knowledge Keeper query(s)");
      };

      var result = await executor.ExecuteAsync();

      Console.WriteLine($"\n🏆 Debate completed in {result.TotalDuration.TotalSeconds:F1}s");
      Console.WriteLine($"📁 Saved to: ./results/rag_debate.md");

      if (!string.IsNullOrWhiteSpace(result.FinalVerdict))
      {
         Console.WriteLine("\n══ FINAL VERDICT ══\n");
         Console.WriteLine(result.FinalVerdict);
      }
   }
}
