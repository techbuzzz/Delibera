using System.Reflection;
using System.Text;

namespace Delibera.ConsoleApp;

/// <summary>
///    A discoverable example entry exposed by the interactive Spectre.Console menu.
/// </summary>
/// <param name="Id">Stable identifier used as the CLI flag (e.g. <c>chatclient</c> → <c>--chatclient</c>).</param>
/// <param name="Title">Human-friendly title shown in the menu.</param>
/// <param name="Description">Short one-line description shown under the title.</param>
/// <param name="Category">Logical grouping used to organise the menu.</param>
/// <param name="Order">Sort order within a category (lower = earlier).</param>
/// <param name="Aliases">Additional CLI flags that also invoke this example (e.g. <c>di</c> for the DI example).</param>
/// <param name="RunAsync">Delegate that runs the example.</param>
public sealed record ExampleEntry(
   string Id,
   string Title,
   string Description,
   string Category,
   int Order,
   IReadOnlyList<string> Aliases,
   Func<CancellationToken, Task> RunAsync);

/// <summary>
///    Dynamically discovers every example in <c>Delibera.ConsoleApp.Examples</c> by scanning
///    for public static <c>RunAsync()</c> methods. Curated metadata (title, description,
///    category, explicit id, aliases) is keyed by class name; unknown examples fall back
///    to sensible defaults so adding a new example file requires zero changes here.
/// </summary>
public static class ExampleRegistry
{
   // (Title, Description, Category, Order, explicit Id?, aliases[])
   private static readonly Dictionary<string, (string Title, string Description, string Category, int Order, string? Id, string[] Aliases)> Metadata =
      new(StringComparer.OrdinalIgnoreCase)
      {
         ["QuickStart"] = ("Quick Start", "Minimal programmatic council — no appsettings required.", "Getting Started", 0, "quick", []),
         ["DependencyInjectionExample"] = ("Dependency Injection", "AddDelibera() DI registration, options binding, resolved services.", "Core", 0, "di", []),
         ["MultiProviderExample"] = ("Multi-Provider Council", "Mix models from different LLM providers in one council.", "Core", 1, "multiprovider", ["multi-provider"]),
         ["CompressionExample"] = ("Context Compression", "All 4 strategies, cache, token counting, council integration.", "Features", 0, "compression", []),
         ["AutoChunkingExample"] = ("AutoChunking", "Progressive disclosure for large documents across rounds.", "Features", 1, "autochunking", ["auto-chunking"]),
         ["CancellationExample"] = ("Cooperative Cancellation", "Ctrl+C / CancellationToken across the whole pipeline.", "Features", 2, "cancellation", []),
         ["SeparateFilesExample"] = ("Separate File Output", "Export result.md, statistics.md, logs.md independently.", "Features", 3, "separate-files", []),
         ["ResilienceExample"] = ("Resilience (Polly v8)", "Named HttpClients + retry pipelines via Microsoft.Extensions.Http.Resilience.", "Infrastructure", 0, "resilience", []),
         ["RagExample"] = ("RAG — Qdrant", "Qdrant-backed Knowledge Keeper with semantic retrieval.", "RAG", 0, "rag", []),
         ["PgVectorExample"] = ("RAG — pgvector", "PostgreSQL/pgvector-backed Knowledge Keeper.", "RAG", 1, "pgvector", ["pg-vector"]),
         ["OperatorExample"] = ("Operator (MCP)", "Operator micro-agent delegating tasks to MCP tools.", "Advanced", 0, "operator", []),
         ["OperatorMcpToolsExample"] = ("Operator + MCP Tools", "Full MCP tool wiring with the Operator role.", "Advanced", 1, "operator-mcp", ["operator-mcp-tools"]),
         ["MicrosoftExtensionsAiExample"] = ("M.E.AI Integration", "IChatClient ↔ ILLMProvider bridges + middleware + council.", "Microsoft.Extensions.AI", 0, "msai", ["microsoft-extensions-ai"]),
         ["ChatClientLLMProviderExample"] = ("ChatClientLLMProvider", "OpenAI/Azure/Ollama via Microsoft.Extensions.AI + streaming.", "Microsoft.Extensions.AI", 1, "chatclient", ["chat-client-llm-provider"]),
         ["TelemetryExample"] = ("Telemetry (OpenTelemetry)", "In-process ActivityListener + MeterListener printing spans & metrics.", "Observability", 0, "telemetry", []),
         ["QuickWinsExample"] = ("Quick Wins Bundle", "HTML export, timeout, personas, benchmark, participant limit.", "Features", 4, "quick-wins", ["quickwins"]),
         ["TemplatesExample"] = ("Debate Templates", "Pre-configured councils: ArchitectureReview, RiskAssessment, CodeReview, …", "Features", 5, "templates", []),
         ["StreamingCouncilExample"] = ("Streaming Council", "IAsyncEnumerable<DebateRound> — rounds yielded live as they complete.", "Features", 6, "stream", ["streaming"]),
         ["AdaptiveStrategyExample"] = ("Adaptive Strategy", "Switch debate strategy mid-flight on stagnation (AdaptiveStrategySelector).", "Features", 7, "adaptive-strategy", []),
         ["VotingExample"] = ("Voting Engine", "Pluggable vote/consensus: Majority, BordaCount, Weighted strategies.", "Features", 8, "voting", []),
         ["StructuredOutputExample"] = ("Structured Output", "JSON-schema-validated typed verdicts from the Chairman.", "Features", 9, "structured-output", []),
         ["PersistenceExample"] = ("Debate Persistence", "Checkpoint after every round; resume from last completed round.", "Features", 10, "persistence", []),
         ["AgentMemoryExample"] = ("Agent Memory", "Council members recall + persist conclusions across sessions.", "Features", 11, "agent-memory", []),
         ["MultiModalExample"] = ("Multi-Modal Council", "Images, diagrams, and documents via pluggable IFileContentReader.", "Features", 12, "multimodal", [])
      };

   /// <summary>Discovers all examples in the Examples namespace, ordered by category then order.</summary>
   public static IReadOnlyList<ExampleEntry> Discover()
   {
      const string examplesNamespace = "Delibera.ConsoleApp.Examples";
      var assembly = Assembly.GetExecutingAssembly();

      var entries = new List<ExampleEntry>();
      foreach (var type in assembly.GetTypes())
      {
         // Example classes are `public static class`, which compile to abstract+sealed.
         // Allow both instance and static classes; only skip compiler-generated closures.
         if (!type.IsClass) continue;
         if (type.IsGenericTypeDefinition) continue;
         if (type.Name.StartsWith('<') || type.Name.StartsWith("<>")) continue;
         if (type.Namespace is null || !type.Namespace.StartsWith(examplesNamespace, StringComparison.Ordinal)) continue;

         var method = type.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static, null, [typeof(CancellationToken)], null) ?? type.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static, null, [], null);
         if (method is null) continue;
         if (method.ReturnType != typeof(Task) && method.ReturnType != typeof(ValueTask)) continue;

         string title, desc, category;
         int order;
         string? explicitId;
         string[] aliases;
         if (Metadata.TryGetValue(type.Name, out var meta))
         {
            (title, desc, category, order, explicitId, aliases) = meta;
         }
         else
         {
            title = SpaceOut(type.Name);
            desc = type.Name;
            category = "Other";
            order = 100;
            explicitId = null;
            aliases = [];
         }

         // Prefer the curated explicit id; otherwise derive a kebab id from the class name.
         var id = !string.IsNullOrWhiteSpace(explicitId)
            ? explicitId!
            : ToKebab(type.Name.EndsWith("Example", StringComparison.Ordinal)
               ? type.Name[..^7]
               : type.Name);

         // Build a delegate accepting a CancellationToken. For examples whose RunAsync()
         // signature is parameterless, we ignore the token (they own their own Ctrl+C).
         Func<CancellationToken, Task> run = method.GetParameters().Length == 0
            ? _ => (Task)method.Invoke(null, null)!
            : ct => (Task)method.Invoke(null, [ct])!;

         entries.Add(new ExampleEntry(id, title, desc, category, order, aliases, run));
      }

      return entries
         .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
         .ThenBy(e => e.Order)
         .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
         .ToList();
   }

   /// <summary>Matches a CLI flag (without the leading <c>--</c>) against an entry's id or aliases.</summary>
   public static ExampleEntry? Find(this IReadOnlyList<ExampleEntry> entries, string flag)
   {
      var comparer = StringComparer.OrdinalIgnoreCase;
      return entries.FirstOrDefault(e => comparer.Equals(e.Id, flag) || e.Aliases.Contains(flag, comparer));
   }

   /// <summary>Converts <c>"ChatClientLLMProvider"</c> → <c>"chatclient-llm-provider"</c>.</summary>
   private static string ToKebab(string s)
   {
      var sb = new StringBuilder(s.Length + 4);
      for (var i = 0; i < s.Length; i++)
      {
         var c = s[i];
         if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]))
            sb.Append('-');
         sb.Append(char.ToLowerInvariant(c));
      }

      return sb.ToString();
   }

   /// <summary>Converts <c>"QuickStart"</c> → <c>"Quick Start"</c>.</summary>
   private static string SpaceOut(string s)
   {
      var sb = new StringBuilder(s.Length + 4);
      for (var i = 0; i < s.Length; i++)
      {
         var c = s[i];
         if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]))
            sb.Append(' ');
         sb.Append(c);
      }

      return sb.ToString();
   }
}
