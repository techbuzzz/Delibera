using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Operator example — demonstrates the 🛠️ Operator role: a micro-agent that connects to one or
///    more MCP (Model Context Protocol) servers and exposes their tools to the debate participants.
///    During the debate, participants delegate natural-language tasks to the Operator using the
///    <c>[[OPERATOR: ...]]</c> marker (e.g., web search, database lookups, writing to Notion, etc.).
///    The Operator uses its own (cheaper) model to plan tool calls, execute them, and synthesise an
///    answer that is fed back into the next round.
/// </summary>
public static class OperatorExample
{
   public static async Task RunAsync()
   {
      Console.WriteLine("🛠️  Operator (MCP) Example\n");

      // ── 1. LLM providers ──
      // Participants typically use stronger (more expensive) models; the Operator uses a cheaper one.
      using var factory = new ProviderFactory();
      var ollama = factory.CreateOllama("http://localhost:11434");

      // ── 2. Configure MCP servers ──
      // (a) Stdio transport — a local MCP server launched as a child process.
      //     The "everything" reference server ships a handful of demo tools (echo, add, etc.).
      var everythingServer = McpServerConfig.Stdio(
         "demo",
         "npx",
         ["-y", "@modelcontextprotocol/server-everything"]);

      // (b) A filesystem MCP server scoped to a working directory (also stdio).
      var filesystemServer = McpServerConfig.Stdio(
         "files",
         "npx",
         ["-y", "@modelcontextprotocol/server-filesystem", Directory.GetCurrentDirectory()]);

      // (c) HTTP/SSE transport — a remote MCP server (e.g., a hosted Notion or web-search server).
      //     Supply auth headers as needed. Commented out: requires a real endpoint + token.
      // var notionServer = McpServerConfig.Http(
      //    name: "notion",
      //    endpoint: new Uri("https://mcp.notion.com/sse"),
      //    additionalHeaders: new Dictionary<string, string> { ["Authorization"] = "Bearer <token>" });

      var servers = new[] { everythingServer, filesystemServer };

      // ── 3. Build the council with an Operator ──
      // The convenience overload creates the Operator from a (cheap) model + MCP servers and wires
      // it through the whole pipeline. The Operator is DI-friendly and initialised automatically
      // (connects to the servers and discovers their tools) before the debate starts.
      var executor = new CouncilBuilder()
         .AddMember("qwen2.5", ollama, "Researcher")
         .AddMember("llama2", ollama, "Analyst")
         .SetChairman(Chairman.CreateStandard("qwen2.5", ollama))
         .WithOperator(
            "llama3.2", // cheaper model for tool orchestration
            ollama,
            servers)
         .WithStandardDebate()
         .WithSystemPrompt("You are a meticulous research council. Delegate factual lookups to the Operator.")
         .WithUserPrompt(
            "Summarise the key risks of adopting microservices for a 4-developer startup. " +
            "If you need an external lookup or calculation, delegate it to the Operator.")
         .WithMaxRounds(4)
         .SaveResultTo("./results/operator_debate.md")
         .Build();

      Console.WriteLine(executor.GetInfo());

      // ── 4. Observe Operator activity per round ──
      executor.OnRoundCompleted += round =>
      {
         Console.WriteLine($"✅ Round {round.RoundNumber}: {round.RoundName}");
         foreach (var oi in round.OperatorInteractions)
         {
            Console.WriteLine($"   🛠️  {oi.RequesterName} → \"{oi.Task}\"");
            Console.WriteLine($"       tools: {oi.ToolCallCount}, answer: {Preview(oi.Answer, 120)}");
         }
      };

      // ── 5. Run the debate ──
      try
      {
         var result = await executor.ExecuteAsync();

         Console.WriteLine($"\n🏆 Debate completed in {result.TotalDuration.TotalSeconds:F1}s");
         if (result.OperatorName is not null)
            Console.WriteLine($"   Operator: {result.OperatorName}");
         Console.WriteLine("📁 Saved to: ./results/operator_debate.md");

         if (!string.IsNullOrWhiteSpace(result.FinalVerdict))
         {
            Console.WriteLine("\n══ FINAL VERDICT ══\n");
            Console.WriteLine(result.FinalVerdict);
         }
      }
      catch (Exception ex)
      {
         Console.WriteLine($"\n❌ Operator example failed: {ex.Message}");
         Console.WriteLine("\n💡 Tips:");
         Console.WriteLine("   • Node.js + npx must be installed for the npx-based MCP servers.");
         Console.WriteLine("   • Ensure a local Ollama server is running: ollama serve");
         Console.WriteLine("   • Swap in your own MCP servers via McpServerConfig.Stdio / .Http.");
      }
   }

   private static string Preview(string text, int max)
   {
      return string.IsNullOrEmpty(text) ? "(empty)" : text.Length <= max ? text : text[..max] + "…";
   }
}