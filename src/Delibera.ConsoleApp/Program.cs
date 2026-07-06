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
using Spectre.Console;

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

      // Default Ctrl+C handling: cancel the in-flight debate cooperatively
      // instead of letting SIGINT terminate the process mid-round.
      using var appCts = new CancellationTokenSource();
      Console.CancelKeyPress += (_, e) =>
      {
         e.Cancel = true;
         if (!appCts.IsCancellationRequested)
         {
            AnsiConsole.MarkupLine("\n[yellow]⚠️  Ctrl+C detected — canceling the debate...[/]");
            try { appCts.Cancel(); } catch (ObjectDisposedException) { /* race */ }
         }
      };

      try
      {
         await RunAsync(args, appCts.Token).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
         PrintFatalError(ex);
         WaitForKeyOnExit("[red]Press any key to exit…[/]", isError: true);
         return;
      }

      WaitForKeyOnExit("\n[green]🏁 Delibera session complete. Press any key to exit…[/]", isError: false);
   }

   private static async Task RunAsync(string[] args, CancellationToken ct)
   {
      // ═══════════════════════════════════════════════
      // 🆕 v3.1: DI & Separate Files Examples
      // ═══════════════════════════════════════════════

      // 1. Discover every example in the Examples/ folder dynamically.
      var examples = ExampleRegistry.Discover();

      // 2. If a CLI flag matches an example id or alias (e.g. "--chatclient"), run it directly.
      ExampleEntry? matched = null;
      foreach (var flag in args.Where(a => a.StartsWith("--", StringComparison.Ordinal)))
      {
         matched = examples.Find(flag[2..]);
         if (matched is not null) break;
      }

      if (matched is not null)
      {
         await RunExampleAsync(matched, ct);
         return;
      }

      // 3. "--list" prints the catalog as a Spectre table (useful for scripts / CI).
      if (args.Contains("--list"))
      {
         PrintExampleCatalog(examples);
         return;
      }

      // 4. Interactive Spectre.Console menu (when stdin is a TTY and no flag was given).
      if (IsInteractiveConsole && args.Length == 0)
      {
         var selection = ShowExampleMenu(examples);
         if (selection is null)
            return;
         await RunExampleAsync(selection, ct);
         return;
      }

      // 5. Default demo: full config-driven council debate (reads appsettings.json).
      await RunDefaultDebateAsync(args, ct);
   }

   // ─── Example dispatch ──────────────────────────────────────────────────────

   private static async Task RunExampleAsync(ExampleEntry example, CancellationToken ct)
   {
      AnsiConsole.Write(new Rule($"[bold green]{example.Title}[/]")
      {
         Style = Style.Parse("green dim")
      });
      AnsiConsole.MarkupLine($"[dim]{example.Description}[/]");
      AnsiConsole.WriteLine();

      try
      {
         await example.RunAsync(ct);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
         AnsiConsole.MarkupLine("\n[yellow]🛑 Cancelled by user (Ctrl+C).[/]");
      }
      catch (Exception ex)
      {
         PrintFatalError(ex, $"❌ Example '{example.Title}' failed");
         throw;
      }
   }

   // ─── Interactive menu ────────────────────────────────────────────────────────

   private static ExampleEntry? ShowExampleMenu(IReadOnlyList<ExampleEntry> examples)
   {
      // Group by category, present as a flat selection list with section dividers.
      var choices = new List<string>();
      var choiceToEntry = new Dictionary<string, ExampleEntry>();
      string? lastCategory = null;

      foreach (var ex in examples)
      {
         if (ex.Category != lastCategory)
         {
            choices.Add($"── {ex.Category} ──");
            lastCategory = ex.Category;
         }
         var label = $"  {ex.Title}";
         choices.Add(label);
         choiceToEntry[label] = ex;
      }

      choices.Add("── Default ──");
      choices.Add("  Full Council Debate (appsettings.json)");

      var selected = AnsiConsole.Prompt(
         new SelectionPrompt<string>()
            .Title("Choose a [green]Delibera example[/] to run:")
            .PageSize(20)
            .MoreChoicesText("[grey](Move up and down to see more)[/]")
            .AddChoices(choices));

      // Divider headers are not runnable; treat them as no-ops.
      if (selected.Contains("Full Council Debate", StringComparison.OrdinalIgnoreCase))
         return null;

      return choiceToEntry.TryGetValue(selected, out var entry) ? entry : null;
   }

   private static void PrintExampleCatalog(IReadOnlyList<ExampleEntry> examples)
   {
      var table = new Table()
         .BorderColor(Color.Green)
         .AddColumn(new TableColumn("[bold]Flag[/]"))
         .AddColumn(new TableColumn("[bold]Aliases[/]"))
         .AddColumn(new TableColumn("[bold]Title[/]"))
         .AddColumn(new TableColumn("[bold]Category[/]"))
         .AddColumn(new TableColumn("[bold]Description[/]"));

      foreach (var e in examples)
         table.AddRow($"--{e.Id}", string.Join(", ", e.Aliases), e.Title, e.Category, e.Description);

      AnsiConsole.Write(table);
      AnsiConsole.MarkupLine("[grey]Run with any flag above, or no args for the interactive menu.[/]");
   }

   // ─── Default config-driven debate ──────────────────────────────────────────

   private static async Task RunDefaultDebateAsync(string[] args, CancellationToken ct)
   {
      // Quick DI showcase before main demo
      AnsiConsole.MarkupLine("🆕 [bold]v3.1 DI Quick Demo:[/]");
      AnsiConsole.MarkupLine("   Run with [blue]--list[/] to see all available examples");
      AnsiConsole.MarkupLine("   Run with [blue]--di[/] for full DI example");
      AnsiConsole.MarkupLine("   Run with [blue]--separate-files[/] for file output demo");
      AnsiConsole.MarkupLine("   Run with [blue]--autochunking[/] for AutoChunking demo (large documents)");
      AnsiConsole.MarkupLine("   Run with [blue]--cancellation[/] for cooperative cancellation demo (Ctrl+C)");
      AnsiConsole.MarkupLine("   Run with [blue]--chatclient[/] for ChatClientLLMProvider (M.E.AI) demo\n");

      // ═══════════════════════════════════════════════
      // 1. Load configuration
      // ═══════════════════════════════════════════════
      AnsiConsole.MarkupLine("📋 [bold]Loading configuration...[/]");

      var configuration = new ConfigurationBuilder()
         .SetBasePath(Directory.GetCurrentDirectory())
         .AddJsonFile("appsettings.json", false, true)
         .AddUserSecrets(Assembly.GetEntryAssembly()!)
         .Build();

      var cfg = configuration.GetSection("DeliberaApp");

      // ═══════════════════════════════════════════════
      // 2. Initialise LLM providers
      // ═══════════════════════════════════════════════
      AnsiConsole.MarkupLine("🔧 [bold]Initialising LLM providers...[/]\n");

      using var factory = new ProviderFactory();
      var providers = new Dictionary<string, ILLMProvider>();

      foreach (var sec in cfg.GetSection("Providers").GetChildren())
      {
         var name = sec.Key;
         var type = sec["Type"] ?? "Ollama";
         AnsiConsole.MarkupLine($"  ✦ [bold]{name}[/] (Type: {type}, Endpoint: {sec["Endpoint"]})");

         providers[name] = factory.Create(name, type, sec);
      }

      AnsiConsole.MarkupLine($"\n  ✅ [green]{providers.Count} provider(s) initialised[/]");

      // Check availability
      AnsiConsole.MarkupLine("\n🏥 [bold]Checking provider availability...[/]");
      foreach (var (name, prov) in providers)
         try
         {
            ct.ThrowIfCancellationRequested();
            var ok = await prov.IsAvailableAsync(ct);
            AnsiConsole.MarkupLine($"  {name}: {(ok ? "[green]✅ Available[/]" : "[red]❌ Unavailable[/]")}");
            if (!ok) continue;
            var models = await prov.ListModelsAsync(ct);
            AnsiConsole.MarkupLine($"    Models: {string.Join(", ", models.Take(10))}");
         }
         catch (Exception ex)
         {
            AnsiConsole.MarkupLine($"  {name}: [yellow]⚠️  {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("    [dim](Expected if Ollama Cloud API key is not configured)[/]");
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
               AnsiConsole.MarkupLine("\n📚 [bold]Setting up RAG with Qdrant...[/]");
               try
               {
                  var ragFactory = new RagProviderFactory();
                  activeRagProvider = ragFactory.CreateQdrant(
                     embeddingProvider,
                     qdrantCfg["Host"] ?? "localhost",
                     qdrantCfg.GetValue<int?>("Port") ?? 6334);
                  AnsiConsole.MarkupLine("  [green]✅ Qdrant RAG ready[/]");
               }
               catch (Exception ex)
               {
                  AnsiConsole.MarkupLine($"  [yellow]⚠️  Qdrant: {Markup.Escape(ex.Message)}[/]");
               }
            }

            // Fallback to pgvector
            if (activeRagProvider is null && pgCfg.Exists())
            {
               AnsiConsole.MarkupLine("\n📚 [bold]Setting up RAG with pgvector...[/]");
               try
               {
                  var connStr = pgCfg["ConnectionString"] ?? "Host=localhost;Database=council_vectors;Username=postgres;Password=postgres";
                  var ragFactory = new RagProviderFactory();
                  activeRagProvider = ragFactory.CreatePgVector(embeddingProvider, connStr);
                  AnsiConsole.MarkupLine("  [green]✅ pgvector RAG ready[/]");
               }
               catch (Exception ex)
               {
                  AnsiConsole.MarkupLine($"  [yellow]⚠️  pgvector: {Markup.Escape(ex.Message)}[/]");
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
                     ct.ThrowIfCancellationRequested();
                     var chunks = await knowledgeKeeper.IndexFileAsync(file, ct: ct);
                     AnsiConsole.MarkupLine($"  📄 Indexed: {file} ({chunks} chunks)");
                  }
                  catch (Exception ex)
                  {
                     AnsiConsole.MarkupLine($"  [yellow]⚠️  Could not index {file}: {Markup.Escape(ex.Message)}[/]");
                  }

               AnsiConsole.MarkupLine($"  [green]✅ Knowledge Keeper ready[/] ({activeRagProvider.ProviderName})");
            }
         }
      }

      // ═══════════════════════════════════════════════
      // 4. Load legacy knowledge base (Markdown)
      // ═══════════════════════════════════════════════
      IKnowledgeBase? knowledgeBase = null;
      if (cfg.GetValue<bool>("Knowledge:Enabled") && knowledgeKeeper is null)
      {
         AnsiConsole.MarkupLine("\n📚 [bold]Loading Markdown knowledge base...[/]");
         var kb = new MarkdownKnowledgeBase("Council Knowledge");

         foreach (var file in cfg.GetSection("Knowledge:Files").Get<string[]>() ?? [])
            try
            {
               ct.ThrowIfCancellationRequested();
               await kb.LoadAsync(file, ct);
               AnsiConsole.MarkupLine($"  📄 Loaded: {file}");
            }
            catch (FileNotFoundException)
            {
               AnsiConsole.MarkupLine($"  [yellow]⚠️  Not found: {file}[/]");
            }
            catch (OperationCanceledException)
            {
               throw;
            }

         if (kb.DocumentCount > 0)
         {
            knowledgeBase = kb;
            AnsiConsole.MarkupLine($"  [green]✅ {kb.DocumentCount} document(s), {kb.TotalCharacters} chars[/]");
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
         AnsiConsole.MarkupLine("\n🗜️  [bold]Setting up Context Compression...[/]");
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

            AnsiConsole.MarkupLine($"  Strategy: {compressor.StrategyName}");
            AnsiConsole.MarkupLine($"  Target ratio: {compressionOptions.TargetRatio:P0}");
            if (compressionCache is not null)
               AnsiConsole.MarkupLine($"  Cache: enabled (max {compressionCache.Count} entries)");
            AnsiConsole.MarkupLine("  [green]✅ Compression ready[/]");
         }
         catch (Exception ex)
         {
            AnsiConsole.MarkupLine($"  [yellow]⚠️  Compression setup failed: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("  [dim]ℹ️  Proceeding without compression[/]");
         }
      }

      // ═══════════════════════════════════════════════
      // 6. Build the Council
      // ═══════════════════════════════════════════════
      AnsiConsole.MarkupLine("\n🏛️  [bold]Building the Council...[/]\n");

      var debateCfg = cfg.GetSection("Debate");
      var stratName = debateCfg["Strategy"] ?? "Standard";
      var maxRounds = debateCfg.GetValue<int?>("MaxRounds") ?? 4;
      var temperature = debateCfg.GetValue<float?>("Temperature") ?? 0.7f;

      var systemPrompt = cfg["Prompts:SystemPrompt"] ?? "You are a helpful AI assistant participating in a council debate.";
      var userPrompt = cfg["Prompts:UserPrompt"] ?? "What is the different between Microservices vs Monolith?";
      var responseLanguage = debateCfg["ResponseLanguage"];
      var maxDegreeOfParallelism = debateCfg.GetValue<int?>("MaxDegreeOfParallelism") ?? 0;

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
         .WithTemperature(temperature)
         .WithResponseLanguage(responseLanguage)
         .WithMaxDegreeOfParallelism(maxDegreeOfParallelism);

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
            AnsiConsole.MarkupLine($"  👤 {modelName} ({provName}) [{role}]");
         }
         else
         {
            AnsiConsole.MarkupLine($"  [yellow]⚠️  Provider '{provName}' not found for '{modelName}'[/]");
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
         AnsiConsole.MarkupLine($"  ★  Chairman: {chairModel} ({chairProv}) [{chairType}]");
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
         AnsiConsole.MarkupLine($"  🗜️  Compression: {compressor.StrategyName}");
      }

      // Output
      var outputDir = debateCfg["OutputDirectory"] ?? "./debate_results";
      Directory.CreateDirectory(outputDir);
      var outputFile = Path.Combine(outputDir, $"debate_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
      builder.SaveResultTo(outputFile);

      var executor = builder.Build();
      AnsiConsole.MarkupLine($"\n{Markup.Escape(executor.GetInfo())}");

      // ═══════════════════════════════════════════════
      // 7. Run the debate
      // ═══════════════════════════════════════════════
      AnsiConsole.MarkupLine("🎯 [bold]Starting debate...[/]\n");
      AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("green dim")));

      // Stream every ExecutionLog entry live so the user can watch progress in real time.
      executor.OnLog += entry => WriteLogEntry(entry);

      // Surface non-fatal internal errors (e.g. failed MCP tool call) without aborting the debate.
      executor.OnError += (ex, context) => { WriteErrorEntry(ex, context); };

      executor.OnRoundCompleted += round =>
      {
         AnsiConsole.MarkupLine($"\n[green]✅ Round {round.RoundNumber}: {round.RoundName}[/] [dim]({round.Duration.TotalSeconds:F1}s)[/]");
         AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("grey dim")));

         if (round.KnowledgeInteractions.Count > 0)
         {
            AnsiConsole.MarkupLine("  📚 Knowledge Keeper interactions:");
            foreach (var ki in round.KnowledgeInteractions)
               AnsiConsole.MarkupLine($"    Q: {Markup.Escape(ki.Query[..Math.Min(80, ki.Query.Length)])}… → {ki.SourceChunks} chunks");
         }

         if (round.OperatorInteractions.Count > 0)
         {
            AnsiConsole.MarkupLine("  🛠️  Operator interactions:");
            foreach (var oi in round.OperatorInteractions)
               AnsiConsole.MarkupLine($"    {oi.RequesterName}: {Markup.Escape(oi.Task[..Math.Min(80, oi.Task.Length)])}… → {oi.ToolCallCount} tool call(s)");
         }

         foreach (var (member, response) in round.Responses)
         {
            var preview = response.Length > 300
               ? response[..300] + "…"
               : response;
            AnsiConsole.MarkupLine($"\n  📝 [bold]{Markup.Escape(member)}[/]:");
            AnsiConsole.MarkupLine($"     {Markup.Escape(preview).Replace("\n", "\n     ")}");
         }

         AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("grey dim")));
      };

      try
      {
         var result = await executor.ExecuteAsync(ct);

         AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("green")));
         AnsiConsole.MarkupLine("[bold green]🏆 DEBATE COMPLETED![/]");
         AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("green")));

         AnsiConsole.MarkupLine($"\n  Debate ID:          {result.DebateId}");
         AnsiConsole.MarkupLine($"  Strategy:           {result.StrategyName}");
         AnsiConsole.MarkupLine($"  Rounds:             {result.Rounds.Count}");
         AnsiConsole.MarkupLine($"  Duration:           {result.TotalDuration.TotalSeconds:F1}s");
         AnsiConsole.MarkupLine($"  Participants:       {string.Join(", ", result.Participants)}");
         if (result.KnowledgeKeeperName is not null)
            AnsiConsole.MarkupLine($"  Knowledge Keeper:   {result.KnowledgeKeeperName}");
         if (result.OperatorName is not null)
            AnsiConsole.MarkupLine($"  Operator:           {result.OperatorName}");

         // Token statistics
         if (result.TokenStats is not null) AnsiConsole.MarkupLine($"\n{Markup.Escape(result.TokenStats.ToSummary())}");

         // Compression log summary
         if (result.CompressionLogs.Count > 0)
         {
            AnsiConsole.MarkupLine($"  🗜️  Compression ops: {result.CompressionLogs.Count}");
            foreach (var log in result.CompressionLogs)
               AnsiConsole.MarkupLine($"    R{log.RoundNumber}: {log.Description} — {log.Ratio:P0} ({log.Duration.TotalMilliseconds:F0}ms)");
         }

         // Cache stats
         if (compressionCache is not null)
            AnsiConsole.MarkupLine($"\n  {Markup.Escape(compressionCache.GetSummary())}");

         if (!string.IsNullOrWhiteSpace(result.OpeningStatement))
         {
            AnsiConsole.MarkupLine("\n[bold]══ OPENING STATEMENT ══[/]\n");
            AnsiConsole.MarkupLine(Markup.Escape(result.OpeningStatement));
         }

         if (!string.IsNullOrWhiteSpace(result.FinalVerdict))
         {
            AnsiConsole.MarkupLine("\n[bold]══ FINAL VERDICT ══[/]\n");
            AnsiConsole.MarkupLine(Markup.Escape(result.FinalVerdict));
         }

         // 🆕 v3.1: Save separate files
         AnsiConsole.MarkupLine("\n  📁 Output files:");
         AnsiConsole.MarkupLine($"    Single file: {outputFile}");

         try
         {
            var separateDir = Path.Combine(outputDir, $"debate_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
            var (rp, sp, lp) = await result.SaveAllAsync(separateDir, filePrefix: null, ct: ct);
            AnsiConsole.MarkupLine($"    Result:      {rp}");
            AnsiConsole.MarkupLine($"    Statistics:  {sp}");
            AnsiConsole.MarkupLine($"    Logs:        {lp}");
         }
         catch (Exception saveEx)
         {
            AnsiConsole.MarkupLine($"    [yellow]⚠️  Separate files: {Markup.Escape(saveEx.Message)}[/]");
         }

         // 🆕 v3.1: Execution logs summary
         if (result.ExecutionLogs.Count > 0)
         {
            AnsiConsole.MarkupLine($"\n  📋 Execution Logs ({result.ExecutionLogs.Count} entries):");
            foreach (var log in result.ExecutionLogs.Where(l => l.Level >= ExecutionLogLevel.Info))
               AnsiConsole.MarkupLine($"    {Markup.Escape(log.ToString())}");
         }
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
         AnsiConsole.MarkupLine("\n[yellow]🛑 Debate cancelled by user (Ctrl+C or token).[/]");
         // Don't print the full fatal-error panel for a clean cancel.
      }
      catch (Exception ex)
      {
         PrintFatalError(ex, "❌ Debate failed");
         AnsiConsole.MarkupLine("\n💡 [bold]Tips:[/]");
         AnsiConsole.MarkupLine("   • Ensure Ollama Cloud API key is set in appsettings.json");
         AnsiConsole.MarkupLine("   • Or run a local Ollama server: ollama serve");
         AnsiConsole.MarkupLine("   • For Qdrant RAG: docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant");
         AnsiConsole.MarkupLine("   • For pgvector RAG: PostgreSQL 15+ with CREATE EXTENSION vector;");
         throw;
      }
   }

   // ─── Banner & observability helpers ──────────────────────────────────────────

   private static void PrintBanner()
   {
      AnsiConsole.Write(new FigletText("Delibera")
      {
         Color = Color.Green
      });
      AnsiConsole.Write(new Text("   ⚖️  Thoughtful AI Decisions  ·  v3.1", new Style(Color.Grey))
      {
         Justification = Justify.Center
      });
      AnsiConsole.WriteLine();
      AnsiConsole.Write(new Text("RAG • pgvector • Knowledge Keeper • Chairman", new Style(Color.Grey))
      {
         Justification = Justify.Center
      });
      AnsiConsole.WriteLine();
      AnsiConsole.Write(new Text("Context Compression • DI • Execution Logging", new Style(Color.Grey))
      {
         Justification = Justify.Center
      });
      AnsiConsole.WriteLine();
      AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("green dim")));
      AnsiConsole.WriteLine();
   }

   /// <summary>
   ///    Writes a single <see cref="ExecutionLog" /> entry to the console in a
   ///    colour-coded, single-line format. Safe to call from the executor's
   ///    streaming events.
   /// </summary>
   /// <remarks>
   ///    Uses <see cref="Console" /> rather than <c>AnsiConsole.MarkupLine</c> because the
   ///    log text may contain arbitrary model output (including Spectre markup characters)
   ///    and runs from a non-UI thread where Spectre's single-line writer is not safe.
   /// </remarks>
   private static void WriteLogEntry(ExecutionLog entry)
   {
      var prev = Console.ForegroundColor;
      Console.ForegroundColor = entry.Level switch
      {
         ExecutionLogLevel.Trace => ConsoleColor.DarkGray,
         ExecutionLogLevel.Info => ConsoleColor.Cyan,
         ExecutionLogLevel.Warning => ConsoleColor.Yellow,
         ExecutionLogLevel.Error => ConsoleColor.Red,
         _ => prev
      };

      Console.WriteLine($"  ┊ {entry}");
      Console.ForegroundColor = prev;
   }

   /// <summary>
   ///    Writes a non-fatal internal error to the console with a small stack-trace
   ///    excerpt. The debate continues; this is purely informational.
   /// </summary>
   private static void WriteErrorEntry(Exception ex, string context)
   {
      var prev = Console.ForegroundColor;
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"  ┊ ⚠ {context} error: {ex.Message}");
      if (ex.StackTrace is not null)
      {
         var firstFrame = ex.StackTrace
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
         if (firstFrame is { Length: > 0 } line && FirstLineIsMeaningful(line))
            Console.WriteLine($"  ┊   at {line.Trim()}");
      }

      Console.ForegroundColor = prev;
   }

   /// <summary>
   ///    Prints a full diagnostic panel for a fatal exception using Spectre.Console.
   /// </summary>
   /// <param name="ex">The exception that aborted the run.</param>
   /// <param name="header">Optional header line; defaults to a generic label.</param>
   private static void PrintFatalError(Exception ex, string header = "❌ Unhandled exception")
   {
      var content = new Markup(
         $"[bold]Type:[/] {Markup.Escape(ex.GetType().FullName ?? ex.GetType().Name)}\n" +
         $"[bold]Message:[/] {Markup.Escape(ex.Message)}\n\n" +
         $"[bold]Stack trace:[/]\n{Markup.Escape(ex.StackTrace ?? "(none)")}").LeftJustified();

      AnsiConsole.Write(
         new Panel(content)
         {
            Header = new PanelHeader(header),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red)
         });
   }

   /// <summary>
   ///    Pauses the console so the user can read output before the window closes.
   ///    Honoured in both normal and error paths. When <paramref name="isError" />
   ///    is <c>true</c> the exit code is set to 1.
   /// </summary>
   private static void WaitForKeyOnExit(string prompt, bool isError)
   {
      AnsiConsole.WriteLine();
      AnsiConsole.MarkupLine(prompt);

      try
      {
         Console.ReadKey(true);
      }
      catch (InvalidOperationException)
      {
         // No interactive console (e.g. redirected stdin in CI) — fall back gracefully.
         AnsiConsole.MarkupLine("[grey](no interactive console available; exiting.)[/]");
      }

      Environment.ExitCode = isError
         ? 1
         : 0;
   }

   private static bool IsInteractiveConsole
   {
      get
      {
         try
         {
            return !Console.IsInputRedirected && !Console.IsOutputRedirected;
         }
         catch
         {
            return false;
         }
      }
   }

   private static bool FirstLineIsMeaningful(string? frame)
   {
      if (string.IsNullOrWhiteSpace(frame))
         return false;

      var trimmed = frame.Trim();
      // Filter out noise from runtime/compiler-emitted frames.
      return !trimmed.StartsWith("at System.", StringComparison.Ordinal) || trimmed.Contains("Delibera", StringComparison.Ordinal);
   }
}