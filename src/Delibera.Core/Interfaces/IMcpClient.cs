namespace Delibera.Core.Interfaces;

/// <summary>
///    Thin abstraction over a connection to a single MCP (Model Context Protocol) server.
///    Allows the <see cref="Council.Operator" /> micro-agent to discover and invoke tools
///    without coupling the rest of the framework to a specific MCP SDK, and keeps the
///    Operator unit-testable via fakes.
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
   /// <summary>Logical name of the server (e.g., "web", "notion", "postgres").</summary>
   string ServerName { get; }

   /// <summary>Whether the client has been connected via <see cref="ConnectAsync" />.</summary>
   bool IsConnected { get; }

   /// <summary>
   ///    Establishes the connection to the MCP server and performs the initial handshake.
   /// </summary>
   /// <param name="ct">Cancellation token.</param>
   Task ConnectAsync(CancellationToken ct = default);

   /// <summary>
   ///    Lists the tools exposed by the connected MCP server.
   /// </summary>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Discovered tools, tagged with this client's <see cref="ServerName" />.</returns>
   Task<IReadOnlyList<OperatorTool>> ListToolsAsync(CancellationToken ct = default);

   /// <summary>
   ///    Invokes a tool on the MCP server.
   /// </summary>
   /// <param name="toolName">Name of the tool to call.</param>
   /// <param name="arguments">Arguments passed to the tool.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The tool's textual result and error flag.</returns>
   Task<McpToolResult> CallToolAsync(
      string toolName,
      IReadOnlyDictionary<string, object?> arguments,
      CancellationToken ct = default);
}
