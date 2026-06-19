namespace Delibera.Core.Interfaces;

/// <summary>
///    The Operator — a special council role implemented as a self-contained micro-agent.
///    During a debate, participants can delegate tasks to the Operator in natural language
///    (e.g., "search the web for …", "save the debate summary to Notion", "query the
///    PostgreSQL employee table for …"). The Operator interprets the task with its own
///    (typically cheaper) LLM model, selects and invokes the appropriate MCP tools,
///    interprets the raw tool output, optionally compresses it, and returns a concise
///    answer to the requesting participant.
/// </summary>
public interface IOperator : IAsyncDisposable
{
   /// <summary>Display name shown in debate logs (e.g., "🛠️ Operator (llama3.2)").</summary>
   string DisplayName { get; }

   /// <summary>Whether <see cref="InitializeAsync" /> has completed successfully.</summary>
   bool IsInitialized { get; }

   /// <summary>All tools discovered across the configured MCP servers.</summary>
   IReadOnlyList<OperatorTool> AvailableTools { get; }

   /// <summary>All interactions recorded during this session.</summary>
   IReadOnlyList<OperatorInteraction> Interactions { get; }

   /// <summary>
   ///    Connects to every configured MCP server and discovers their tools.
   ///    Safe to call multiple times — subsequent calls are no-ops once initialised.
   /// </summary>
   /// <param name="ct">Cancellation token.</param>
   Task InitializeAsync(CancellationToken ct = default);

   /// <summary>
   ///    Returns a human-readable catalogue of the Operator's capabilities, suitable for
   ///    injection into participant prompts so they understand what they can delegate.
   /// </summary>
   string GetToolCatalog();

   /// <summary>
   ///    Executes a natural-language task on behalf of a participant: selects MCP tools,
   ///    invokes them, interprets the results, optionally compresses, and returns an answer.
   /// </summary>
   /// <param name="requesterName">Display name of the requesting participant.</param>
   /// <param name="task">The natural-language task to fulfil.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The Operator's result, including the answer and the tool calls performed.</returns>
   Task<OperatorResult> ExecuteTaskAsync(
      string requesterName,
      string task,
      CancellationToken ct = default);
}