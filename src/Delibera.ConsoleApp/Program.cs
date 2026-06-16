using System.Reflection;
using System.Text;
using Delibera.ConsoleApp.Examples;
using Delibera.Core.Compression;
using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Knowledge;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;
using Microsoft.Extensions.Configuration;

namespace Delibera.ConsoleApp;

/// <summary>
///    Delibera v3.1 — demonstration of the full framework:
///    providers, RAG (Qdrant + pgvector), Knowledge Keeper, Chairman,
///    debate strategies, 🔥 Context Compression, and 🆕 Dependency Injection.
/// </summary>
public static class Program
{
   public static async Task Main(string[] args)
   {
      Console.OutputEncoding = Encoding.UTF8;
      PrintBanner();

      // ═══════════════════════════════════════════════
      // 🆕 v3.1: DI & Separate Files Examples
      // ═══════════════════════════════════════════════
      if (args.Contains("--di"))
      {
         await DIExample.RunAsync();
         return;
      }

      if (args.Contains("--separate-files"))
      {
         await SeparateFilesExample.RunAsync();
         return;
      }

      if (args.Contains("--compression"))
      {
         await CompressionExample.RunAsync();
         return;
      }

      if (args.Contains("--multiprovider"))
      {
         await MultiProviderExample.RunAsync();
         return;
      }

      if (args.Contains("--pgvector"))
      {
         await PgVectorExample.RunAsync();
         return;
      }

      if (args.Contains("--rag"))
      {
         await RagExample.RunAsync();
         return;
      }

      if (args.Contains("--operator"))
      {
         await OperatorExample.RunAsync();
         return;
      }

      if (args.Contains("--operator-mcp"))
      {
         await OperatorMcpToolsExample.RunAsync();
         return;
      }

      // Quick DI showcase before main demo
      Console.WriteLine("🆕 v3.1 DI Quick Demo:");
      Console.WriteLine("   Run with --di for full DI example");
      Console.WriteLine("   Run with --separate-files for file output demo\n");

      // ═══════════════════════════════════════════════
      // 1. Load configuration
      // ═══════════════════════════════════════════════
      Console.WriteLine("📋 Loading configuration...");

      var configuration = new ConfigurationBuilder()
         .SetBasePath(Directory.GetCurrentDirectory())
         .AddJsonFile("appsettings.json", false, true)
         .AddUserSecrets(Assembly.GetEntryAssembly()!)
         .Build();

      var cfg = configuration.GetSection("DeliberaApp");

      // ═══════════════════════════════════════════════
      // 2. Initialise LLM providers
      // ═══════════════════════════════════════════════
      Console.WriteLine("🔧 Initialising LLM providers...\n");

      using var factory = new ProviderFactory();
      var providers = new Dictionary<string, ILLMProvider>();

      foreach (var sec in cfg.GetSection("Providers").GetChildren())
      {
         var name = sec.Key;
         var type = sec["Type"] ?? "Ollama";
         Console.WriteLine($"  ✦ {name} (Type: {type}, Endpoint: {sec["Endpoint"]})");

         providers[name] = factory.Create(name, type, sec);
      }

      Console.WriteLine($"\n  ✅ {providers.Count} provider(s) initialised");

      // Check availability
      Console.WriteLine("\n🏥 Checking provider availability...");
      foreach (var (name, prov) in providers)
         try
         {
            var ok = await prov.IsAvailableAsync();
            Console.WriteLine($"  {name}: {(ok ? "✅ Available" : "❌ Unavailable")}");
            if (ok)
            {
               var models = await prov.ListModelsAsync();
               Console.WriteLine($"    Models: {string.Join(", ", models.Take(10))}");
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"  {name}: ⚠️  {ex.Message}");
            Console.WriteLine("    (Expected if Ollama Cloud API key is not configured)");
         }

      // ═══════════════════════════════════════════════
      // 3. Set up RAG / Knowledge Keeper (Qdrant or pgvector)
      // ═══════════════════════════════════════════════
      KnowledgeKeeper? knowledgeKeeper = null;
      IRagProvider? activeRagProvider = null;

      // Try Qdrant first, fallback to pgvector
      var qdrantCfg = cfg.GetSection("Qdrant");
      var pgCfg = cfg.GetSection("PostgreSQL");
      var kkCfg = cfg.GetSection("KnowledgeKeeper");

      if (kkCfg.Exists())
      {
         var kkModelName = kkCfg["Model"] ?? "llama2";
         var kkProviderName = kkCfg["Provider"] ?? "OllamaCloud";
         var embeddingModel = kkCfg["EmbeddingModel"] ?? "llama2";

         if (providers.TryGetValue(kkProviderName, out var kkProv) && kkProv is OllamaProvider ollamaProv)
         {
            var embeddingProvider = new OllamaEmbeddingProvider(ollamaProv, embeddingModel);

            // Try Qdrant
            if (qdrantCfg.Exists())
            {
               Console.WriteLine("\n📚 Setting up RAG with Qdrant...");
               try
               {
                  var ragFactory = new RagProviderFactory();
                  activeRagProvider = ragFactory.CreateQdrant(
                     embeddingProvider,
                     qdrantCfg["Host"] ?? "localhost",
                     qdrantCfg.GetValue<int?>("Port") ?? 6334);
                  Console.WriteLine("  ✅ Qdrant RAG ready");
               }
               catch (Exception ex)
               {
                  Console.WriteLine($"  ⚠️  Qdrant: {ex.Message}");
               }
            }

            // Fallback to pgvector
            if (activeRagProvider is null && pgCfg.Exists())
            {
               Console.WriteLine("\n📚 Setting up RAG with pgvector...");
               try
               {
                  var connStr = pgCfg["ConnectionString"] ?? "Host=localhost;Database=council_vectors;Username=postgres;Password=postgres";
                  var ragFactory = new RagProviderFactory();
                  activeRagProvider = ragFactory.CreatePgVector(embeddingProvider, connStr);
                  Console.WriteLine("  ✅ pgvector RAG ready");
               }
               catch (Exception ex)
               {
                  Console.WriteLine($"  ⚠️  pgvector: {ex.Message}");
               }
            }

            if (activeRagProvider is not null)
            {
               var collectionName = qdrantCfg["CollectionName"] ?? pgCfg["CollectionName"] ?? "council_knowledge";
               var kkMember = new CouncilMember(kkModelName, kkProv, "Knowledge Keeper");
               knowledgeKeeper = new KnowledgeKeeper(activeRagProvider, kkMember, collectionName);

               // Index knowledge files
               var knowledgeFiles = cfg.GetSection("Knowledge:Files").Get<string[]>() ?? [];
               foreach (var file in knowledgeFiles)
                  try
                  {
                     var chunks = await knowledgeKeeper.IndexFileAsync(file);
                     Console.WriteLine($"  📄 Indexed: {file} ({chunks} chunks)");
                  }
                  catch (Exception ex)
                  {
                     Console.WriteLine($"  ⚠️  Could not index {file}: {ex.Message}");
                  }

               Console.WriteLine($"  ✅ Knowledge Keeper ready ({activeRagProvider.ProviderName})");
            }
         }
      }

      // ═══════════════════════════════════════════════
      // 4. Load legacy knowledge base (Markdown)
      // ═══════════════════════════════════════════════
      IKnowledgeBase? knowledgeBase = null;
      if (cfg.GetValue<bool>("Knowledge:Enabled") && knowledgeKeeper is null)
      {
         Console.WriteLine("\n📚 Loading Markdown knowledge base...");
         var kb = new MarkdownKnowledgeBase("Council Knowledge");

         foreach (var file in cfg.GetSection("Knowledge:Files").Get<string[]>() ?? [])
            try
            {
               await kb.LoadAsync(file);
               Console.WriteLine($"  📄 Loaded: {file}");
            }
            catch (FileNotFoundException)
            {
               Console.WriteLine($"  ⚠️  Not found: {file}");
            }

         if (kb.DocumentCount > 0)
         {
            knowledgeBase = kb;
            Console.WriteLine($"  ✅ {kb.DocumentCount} document(s), {kb.TotalCharacters} chars");
         }
      }

      // ═══════════════════════════════════════════════
      // 5. Set up Context Compression 🗜️
      // ═══════════════════════════════════════════════
      IContextCompressor? compressor = null;
      CompressionOptions? compressionOptions = null;
      CompressionCache? compressionCache = null;

      var compCfg = cfg.GetSection("ContextCompression");
      if (compCfg.GetValue<bool>("Enabled"))
      {
         Console.WriteLine("\n🗜️  Setting up Context Compression...");
         var strategyName = compCfg["Strategy"] ?? "Deduplication";

         try
         {
            // Determine available providers for compression
            ILLMProvider? compLlm = null;
            string? compModel = null;
            IEmbeddingProvider? compEmbeddings = null;

            var firstProviderKvp = providers.FirstOrDefault();
            if (firstProviderKvp.Value is OllamaProvider compOllama)
            {
               compLlm = compOllama;
               compModel = cfg.GetSection("Models").GetChildren().FirstOrDefault()?["Name"] ?? "llama2";
               compEmbeddings = new OllamaEmbeddingProvider(compOllama,
                  kkCfg["EmbeddingModel"] ?? compModel);
            }

            compressor = CompressionFactory.Create(strategyName, compLlm, compModel, compEmbeddings);

            compressionOptions = new CompressionOptions
            {
               TargetRatio = compCfg.GetValue<double?>("TargetRatio") ?? 0.5,
               PreserveCodeBlocks = compCfg.GetValue<bool?>("PreserveCodeBlocks") ?? true,
               PreserveStructuredContent = compCfg.GetValue<bool?>("PreserveStructuredContent") ?? true,
               DeduplicationThreshold = compCfg.GetValue<double?>("DeduplicationThreshold") ?? 0.85,
               SummarizationTemperature = compCfg.GetValue<float?>("SummarizationTemperature") ?? 0.3f
            };

            if (compCfg.GetSection("Cache").GetValue<bool>("Enabled"))
            {
               var maxEntries = compCfg.GetSection("Cache").GetValue<int?>("MaxEntries") ?? 256;
               compressionCache = new CompressionCache(maxEntries);
            }

            Console.WriteLine($"  Strategy: {compressor.StrategyName}");
            Console.WriteLine($"  Target ratio: {compressionOptions.TargetRatio:P0}");
            if (compressionCache is not null)
               Console.WriteLine($"  Cache: enabled (max {compressionCache.Count} entries)");
            Console.WriteLine("  ✅ Compression ready");
         }
         catch (Exception ex)
         {
            Console.WriteLine($"  ⚠️  Compression setup failed: {ex.Message}");
            Console.WriteLine("  ℹ️  Proceeding without compression");
         }
      }

      // ═══════════════════════════════════════════════
      // 6. Build the Council
      // ═══════════════════════════════════════════════
      Console.WriteLine("\n🏛️  Building the Council...\n");

      var debateCfg = cfg.GetSection("Debate");
      var stratName = debateCfg["Strategy"] ?? "Standard";
      var maxRounds = debateCfg.GetValue<int?>("MaxRounds") ?? 4;
      var temperature = debateCfg.GetValue<float?>("Temperature") ?? 0.7f;

      var systemPrompt = cfg["Prompts:SystemPrompt"] ?? "You are a helpful AI assistant participating in a council debate.";
      var userPrompt = cfg["Prompts:UserPrompt"] ?? "What is the different between Microservices vs Monolith?";

      IDebateStrategy strategy = stratName.ToLowerInvariant() switch
      {
         "critique" => new CritiqueDebate(),
         "consensus" => new ConsensusDebate(),
         _ => new StandardDebate()
      };

      var builder = new CouncilBuilder()
         .WithStrategy(strategy)
         .WithSystemPrompt(systemPrompt)
         .WithUserPrompt(userPrompt)
         .WithMaxRounds(maxRounds)
         .WithTemperature(temperature);

      // Add members
      foreach (var mc in cfg.GetSection("Models").GetChildren())
      {
         var modelName = mc["Name"] ?? "llama2";
         var provName = mc["Provider"] ?? "OllamaCloud";
         var role = mc["Role"] ?? "Expert";
         var persona = mc["Persona"];

         if (providers.TryGetValue(provName, out var prov))
         {
            builder.AddMember(modelName, prov, role, persona);
            Console.WriteLine($"  👤 {modelName} ({provName}) [{role}]");
         }
         else
         {
            Console.WriteLine($"  ⚠️  Provider '{provName}' not found for '{modelName}'");
         }
      }

      // Chairman
      var chairCfg = cfg.GetSection("Chairman");
      var chairModel = chairCfg["Model"] ?? "qwen2.5";
      var chairProv = chairCfg["Provider"] ?? "OllamaCloud";
      var chairType = chairCfg["Type"] ?? "Standard";

      if (providers.TryGetValue(chairProv, out var cp))
      {
         var chairman = chairType.ToLowerInvariant() switch
         {
            "strict" => Chairman.CreateStrict(chairModel, cp),
            "creative" => Chairman.CreateCreative(chairModel, cp),
            _ => Chairman.CreateStandard(chairModel, cp)
         };
         builder.SetChairman(chairman);
         Console.WriteLine($"  ★  Chairman: {chairModel} ({chairProv}) [{chairType}]");
      }

      // Knowledge
      if (knowledgeKeeper is not null)
         builder.WithKnowledgeKeeper(knowledgeKeeper);
      else if (knowledgeBase is not null)
         builder.WithKnowledge(knowledgeBase);

      // Compression
      if (compressor is not null)
      {
         builder.WithCompression(compressor, compressionOptions);
         if (compressionCache is not null)
            builder.WithCompressionCache();
         Console.WriteLine($"  🗜️  Compression: {compressor.StrategyName}");
      }

      // Output
      var outputDir = debateCfg["OutputDirectory"] ?? "./debate_results";
      Directory.CreateDirectory(outputDir);
      var outputFile = Path.Combine(outputDir, $"debate_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
      builder.SaveResultTo(outputFile);

      var executor = builder.Build();
      Console.WriteLine($"\n{executor.GetInfo()}");

      // ═══════════════════════════════════════════════
      // 7. Run the debate
      // ═══════════════════════════════════════════════
      Console.WriteLine("🎯 Starting debate...\n");
      Console.WriteLine(new string('═', 60));

      executor.OnRoundCompleted += round =>
      {
         Console.WriteLine($"\n✅ Round {round.RoundNumber}: {round.RoundName} ({round.Duration.TotalSeconds:F1}s)");
         Console.WriteLine(new string('─', 50));

         if (round.KnowledgeInteractions.Count > 0)
         {
            Console.WriteLine("  📚 Knowledge Keeper interactions:");
            foreach (var ki in round.KnowledgeInteractions)
               Console.WriteLine($"    Q: {ki.Query[..Math.Min(80, ki.Query.Length)]}… → {ki.SourceChunks} chunks");
         }

         if (round.OperatorInteractions.Count > 0)
         {
            Console.WriteLine("  🛠️  Operator interactions:");
            foreach (var oi in round.OperatorInteractions)
               Console.WriteLine($"    {oi.RequesterName}: {oi.Task[..Math.Min(80, oi.Task.Length)]}… → {oi.ToolCallCount} tool call(s)");
         }

         foreach (var (member, response) in round.Responses)
         {
            var preview = response.Length > 300
               ? response[..300] + "…"
               : response;
            Console.WriteLine($"\n  📝 {member}:");
            Console.WriteLine($"     {preview.Replace("\n", "\n     ")}");
         }

         Console.WriteLine(new string('─', 50));
      };

      try
      {
         var result = await executor.ExecuteAsync();

         Console.WriteLine($"\n{new string('═', 60)}");
         Console.WriteLine("🏆 DEBATE COMPLETED!");
         Console.WriteLine($"{new string('═', 60)}\n");

         Console.WriteLine($"  Debate ID:          {result.DebateId}");
         Console.WriteLine($"  Strategy:           {result.StrategyName}");
         Console.WriteLine($"  Rounds:             {result.Rounds.Count}");
         Console.WriteLine($"  Duration:           {result.TotalDuration.TotalSeconds:F1}s");
         Console.WriteLine($"  Participants:       {string.Join(", ", result.Participants)}");
         if (result.KnowledgeKeeperName is not null)
            Console.WriteLine($"  Knowledge Keeper:   {result.KnowledgeKeeperName}");
         if (result.OperatorName is not null)
            Console.WriteLine($"  Operator:           {result.OperatorName}");

         // Token statistics
         if (result.TokenStats is not null) Console.WriteLine($"\n{result.TokenStats.ToSummary()}");

         // Compression log summary
         if (result.CompressionLogs.Count > 0)
         {
            Console.WriteLine($"  🗜️  Compression ops: {result.CompressionLogs.Count}");
            foreach (var log in result.CompressionLogs)
               Console.WriteLine($"    R{log.RoundNumber}: {log.Description} — {log.Ratio:P0} ({log.Duration.TotalMilliseconds:F0}ms)");
         }

         // Cache stats
         if (compressionCache is not null)
            Console.WriteLine($"\n  {compressionCache.GetSummary()}");

         if (!string.IsNullOrWhiteSpace(result.OpeningStatement))
         {
            Console.WriteLine("\n══ OPENING STATEMENT ══\n");
            Console.WriteLine(result.OpeningStatement);
         }

         if (!string.IsNullOrWhiteSpace(result.FinalVerdict))
         {
            Console.WriteLine("\n══ FINAL VERDICT ══\n");
            Console.WriteLine(result.FinalVerdict);
         }

         // 🆕 v3.1: Save separate files
         Console.WriteLine("\n  📁 Output files:");
         Console.WriteLine($"    Single file: {outputFile}");

         try
         {
            var separateDir = Path.Combine(outputDir, $"debate_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
            var (rp, sp, lp) = await result.SaveAllAsync(separateDir);
            Console.WriteLine($"    Result:      {rp}");
            Console.WriteLine($"    Statistics:  {sp}");
            Console.WriteLine($"    Logs:        {lp}");
         }
         catch (Exception saveEx)
         {
            Console.WriteLine($"    ⚠️  Separate files: {saveEx.Message}");
         }

         // 🆕 v3.1: Execution logs summary
         if (result.ExecutionLogs.Count > 0)
         {
            Console.WriteLine($"\n  📋 Execution Logs ({result.ExecutionLogs.Count} entries):");
            foreach (var log in result.ExecutionLogs.Where(l => l.Level >= LogLevel.Info))
               Console.WriteLine($"    {log}");
         }
      }
      catch (Exception ex)
      {
         Console.WriteLine($"\n❌ Debate failed: {ex.Message}");
         Console.WriteLine("\n💡 Tips:");
         Console.WriteLine("   • Ensure Ollama Cloud API key is set in appsettings.json");
         Console.WriteLine("   • Or run a local Ollama server: ollama serve");
         Console.WriteLine("   • For Qdrant RAG: docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant");
         Console.WriteLine("   • For pgvector RAG: PostgreSQL 15+ with CREATE EXTENSION vector;");
         Console.WriteLine($"\n{ex.StackTrace}");
      }

      Console.WriteLine("\n🏁 Delibera session complete.");
   }

   private static void PrintBanner()
   {
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine("""

                           ██████╗ ███████╗██╗     ██╗██████╗ ███████╗██████╗  █████╗
                           ██╔══██╗██╔════╝██║     ██║██╔══██╗██╔════╝██╔══██╗██╔══██╗
                           ██║  ██║█████╗  ██║     ██║██████╔╝█████╗  ██████╔╝███████║
                           ██║  ██║██╔══╝  ██║     ██║██╔══██╗██╔══╝  ██╔══██╗██╔══██║
                           ██████╔╝███████╗███████╗██║██████╔╝███████╗██║  ██║██║  ██║
                           ╚═════╝ ╚══════╝╚══════╝╚═╝╚═════╝ ╚══════╝╚═╝  ╚═╝╚═╝  ╚═╝

                              ⚖️   Thoughtful AI Decisions   ·   v3.1

                           RAG • pgvector • Knowledge Keeper • Chairman
                           Context Compression • DI • Execution Logging

                        """);
      Console.ResetColor();
   }
}