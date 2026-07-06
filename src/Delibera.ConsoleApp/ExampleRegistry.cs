using System.Reflection;

namespace Delibera.ConsoleApp;

/// <summary>
///    A discoverable example entry exposed by the interactive Spectre.Console menu.
/// </summary>
/// <param name="Id">Stable identifier used as the CLI flag (e.g. <c>chatclient</c> → <c>--chatclient</c>).</param>
/// <param name="Title">Human-friendly title shown in the menu.</param>
/// <param name="Description">Short one-line description shown under the title.</param>
/// <param name="Category">Logical grouping used to organise the menu.</param>
/// <param name="Order">Sort order within a category (lower = earlier).</param>
/// <param name="RunAsync">Delegate that runs the example.</param>
public sealed record ExampleEntry(
   string Id,
   string Title,
   string Description,
   string Category,
   int Order,
   Func<CancellationToken, Task> RunAsync);

/// <summary>
///    Dynamically discovers every example in <c>Delibera.ConsoleApp.Examples</c> by scanning
///    for public static <c>RunAsync()</c> methods. Curated metadata (title, description,
///    category) is keyed by class name; unknown examples fall back to sensible defaults
///    so adding a new example file requires zero changes here.
/// </summary>
public static class ExampleRegistry
{
   private static readonly Dictionary<string, (string Title, string Description, string Category, int Order)> Metadata =
      new(StringComparer.OrdinalIgnoreCase)
      {
         ["QuickStart"] = ("Quick Start", "Minimal programmatic council — no appsettings required.", "Getting Started", 0),
         ["DependencyInjectionExample"] = ("Dependency Injection", "AddDelibera() DI registration, options binding, resolved services.", "Core", 0),
         ["MultiProviderExample"] = ("Multi-Provider Council", "Mix models from different LLM providers in one council.", "Core", 1),
         ["CompressionExample"] = ("Context Compression", "All 4 strategies, cache, token counting, council integration.", "Features", 0),
         ["AutoChunkingExample"] = ("AutoChunking", "Progressive disclosure for large documents across rounds.", "Features", 1),
         ["CancellationExample"] = ("Cooperative Cancellation", "Ctrl+C / CancellationToken across the whole pipeline.", "Features", 2),
         ["SeparateFilesExample"] = ("Separate File Output", "Export result.md, statistics.md, logs.md independently.", "Features", 3),
         ["ResilienceExample"] = ("Resilience (Polly v8)", "Named HttpClients + retry pipelines via Microsoft.Extensions.Http.Resilience.", "Infrastructure", 0),
         ["RagExample"] = ("RAG — Qdrant", "Qdrant-backed Knowledge Keeper with semantic retrieval.", "RAG", 0),
         ["PgVectorExample"] = ("RAG — pgvector", "PostgreSQL/pgvector-backed Knowledge Keeper.", "RAG", 1),
         ["OperatorExample"] = ("Operator (MCP)", "Operator micro-agent delegating tasks to MCP tools.", "Advanced", 0),
         ["OperatorMcpToolsExample"] = ("Operator + MCP Tools", "Full MCP tool wiring with the Operator role.", "Advanced", 1),
         ["MicrosoftExtensionsAiExample"] = ("M.E.AI Integration", "IChatClient ↔ ILLMProvider bridges + middleware + council.", "Microsoft.Extensions.AI", 0),
         ["ChatClientLLMProviderExample"] = ("ChatClientLLMProvider", "OpenAI/Azure/Ollama via Microsoft.Extensions.AI + streaming.", "Microsoft.Extensions.AI", 1)
      };

   /// <summary>Discovers all examples in the Examples namespace, ordered by category then order.</summary>
   public static IReadOnlyList<ExampleEntry> Discover()
   {
      var examplesNamespace = $"{nameof(Delibera)}.{nameof(ConsoleApp)}.Examples";
      var assembly = Assembly.GetExecutingAssembly();

      var entries = new List<ExampleEntry>();
      foreach (var type in assembly.GetTypes())
      {
         if (!type.IsClass || type.IsAbstract) continue;
         if (!type.Namespace?.StartsWith(examplesNamespace, StringComparison.Ordinal) ?? true) continue;

         var method = type.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static, null, [typeof(CancellationToken)], null)
                      ?? type.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static, null, [], null);
         if (method is null) continue;
         if (method.ReturnType != typeof(Task) && method.ReturnType != typeof(ValueTask)) continue;

         var (title, desc, category, order) = Metadata.TryGetValue(type.Name, out var meta)
            ? meta
            : (SpaceOut(type.Name), type.Name, "Other", 100);

         var id = ToKebab(type.Name.EndsWith("Example", StringComparison.Ordinal)
            ? type.Name[..^7] // strip "Example" suffix → "ChatClientLLMProvider"
            : type.Name);

         // Build a delegate accepting a CancellationToken. For examples whose RunAsync()
         // signature is parameterless, we ignore the token (they own their own Ctrl+C).
         Func<CancellationToken, Task> run = method.GetParameters().Length == 0
            ? _ => (Task)method.Invoke(null, null)!
            : ct => (Task)method.Invoke(null, [ct])!;

         entries.Add(new ExampleEntry(id, title, desc, category, order, run));
      }

      return entries
         .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
         .ThenBy(e => e.Order)
         .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
         .ToList();
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