using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
/// Demonstrates the pgvector-backed RAG provider:
/// 1. Creates a PgVectorStore connected to PostgreSQL
/// 2. Creates a PgVectorRagProvider for indexing and search
/// 3. Indexes a sample document
/// 4. Performs semantic search
/// 5. Sets up a Knowledge Keeper with pgvector
/// 6. Runs a council debate with pgvector-backed knowledge
/// </summary>
public static class PgVectorExample
{
   /// <summary>
   /// Runs the pgvector example.
   /// </summary>
   /// <remarks>
   /// <para>Prerequisites:</para>
   /// <list type="bullet">
   ///   <item>PostgreSQL 15+ with pgvector extension: <c>CREATE EXTENSION vector;</c></item>
   ///   <item>Ollama running with a model that supports embeddings</item>
   /// </list>
   /// </remarks>
   public static async Task RunAsync()
   {
      Console.WriteLine("═══════════════════════════════════════");
      Console.WriteLine("  🐘 pgvector RAG Provider Example");
      Console.WriteLine("═══════════════════════════════════════\n");

      // ── 1. Configuration ──
      var connectionString = "Host=localhost;Database=council_vectors;Username=postgres;Password=postgres";
      var ollamaEndpoint = "http://localhost:11434";
      var embeddingModel = "llama2";
      var llmModel = "llama2";

      Console.WriteLine($"  PostgreSQL: {connectionString}");
      Console.WriteLine($"  Ollama: {ollamaEndpoint}");
      Console.WriteLine($"  Embedding model: {embeddingModel}");
      Console.WriteLine($"  LLM model: {llmModel}\n");

      // ── 2. Create providers ──
      var ollamaProvider = new OllamaProvider(ollamaEndpoint);
      var embeddingProvider = new OllamaEmbeddingProvider(ollamaProvider, embeddingModel);

      // Create PgVector RAG provider (will create the table automatically)
      await using var ragProvider = new PgVectorRagProvider(embeddingProvider, connectionString);
      var collectionName = "example_knowledge";

      Console.WriteLine("📦 Creating pgvector collection...");

      // ── 3. Index a sample document ──
      var sampleDocument = """
            # Microservices Architecture

            Microservices is an architectural style that structures an application as a collection
            of loosely coupled services. Each service is fine-grained and implements a single business
            capability. Services communicate via lightweight protocols, typically HTTP/REST or messaging.

            ## Advantages
            - Independent deployment and scaling of services
            - Technology diversity — each service can use different languages/frameworks
            - Fault isolation — failure in one service doesn't cascade
            - Team autonomy — small teams own individual services

            ## Disadvantages
            - Increased operational complexity
            - Distributed system challenges (network latency, data consistency)
            - Service discovery and load balancing overhead
            - Testing becomes more complex (integration, contract tests)

            ## When to Use
            - Large teams (50+ engineers) working on a complex domain
            - Need for independent scaling of different components
            - Polyglot technology requirements
            - Rapid, independent deployment cycles
            """;

      var chunks = await ragProvider.IndexDocumentAsync(
          collectionName,
          sampleDocument,
          metadata: new Dictionary<string, string> { ["source"] = "architecture_guide.md" },
          chunkSize: 300,
          chunkOverlap: 50);

      Console.WriteLine($"  ✅ Indexed {chunks} chunks into '{collectionName}'\n");

      // ── 4. Semantic search ──
      Console.WriteLine("🔍 Semantic search: 'What are microservices disadvantages?'\n");

      var results = await ragProvider.SearchAsync(collectionName, "What are microservices disadvantages?", limit: 3);
      foreach (var r in results)
      {
         Console.WriteLine($"  [{r.Score:F3}] {r.Text[..Math.Min(120, r.Text.Length)]}...");
      }

      // ── 5. Get full context ──
      Console.WriteLine("\n📚 Context retrieval:\n");
      var context = await ragProvider.GetContextAsync(collectionName, "microservices vs monolith tradeoffs", limit: 3);
      Console.WriteLine(context[..Math.Min(500, context.Length)] + "...\n");

      // ── 6. Knowledge Keeper with pgvector ──
      Console.WriteLine("🧠 Setting up Knowledge Keeper with pgvector...\n");

      var kkMember = new CouncilMember(llmModel, ollamaProvider, "Knowledge Keeper");
      var knowledgeKeeper = new KnowledgeKeeper(ragProvider, kkMember, collectionName);

      var answer = await knowledgeKeeper.AnswerQuestionAsync(
          "When should I use microservices instead of a monolith?");

      Console.WriteLine($"  📚 Knowledge Keeper says:\n  {answer[..Math.Min(500, answer.Length)]}...\n");

      // ── 7. Run a debate with pgvector knowledge ──
      Console.WriteLine("🏛️  Running council debate with pgvector knowledge...\n");

      var executor = new CouncilBuilder()
          .AddMember(llmModel, ollamaProvider, "Architect", "You are a pragmatic software architect.")
          .AddMember(llmModel, ollamaProvider, "DevOps Lead", "You focus on operational concerns.")
          .SetChairman(llmModel, ollamaProvider)
          .WithKnowledgeKeeper(knowledgeKeeper)
          .WithStandardDebate()
          .WithUserPrompt("Should our startup (15 engineers) adopt microservices from day one?")
          .WithMaxRounds(2)
          .Build();

      Console.WriteLine(executor.GetInfo());

      var result = await executor.ExecuteAsync();
      Console.WriteLine($"\n  ✅ Debate completed: {result.Rounds.Count} rounds, {result.TotalDuration.TotalSeconds:F1}s");

      if (result.FinalVerdict is not null)
         Console.WriteLine($"\n  📜 Verdict: {result.FinalVerdict[..Math.Min(300, result.FinalVerdict.Length)]}...");

      // ── Cleanup ──
      Console.WriteLine("\n🧹 Cleaning up...");
      await ragProvider.VectorStore.DeleteCollectionAsync(collectionName);
      Console.WriteLine("  ✅ Collection deleted\n");

      Console.WriteLine("═══ pgvector Example Complete ═══\n");
   }
}
