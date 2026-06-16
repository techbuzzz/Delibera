namespace Delibera.Core.Models;

/// <summary>
///    Transport type used to connect to an MCP (Model Context Protocol) server.
/// </summary>
public enum McpTransportType
{
   /// <summary>Local server launched as a child process and communicating over standard I/O.</summary>
   Stdio = 0,

   /// <summary>Remote server reached over HTTP / Server-Sent Events.</summary>
   Http = 1
}

/// <summary>
///    Configuration describing how the <see cref="Council.Operator" /> connects to a single
///    MCP server. Supports both local (stdio) and remote (HTTP/SSE) servers.
/// </summary>
/// <remarks>
///    Examples:
///    <list type="bullet">
///       <item>
///          <description>
///             Stdio: <c>McpServerConfig.Stdio("web", "npx", ["-y", "@modelcontextprotocol/server-everything"])</c>
///          </description>
///       </item>
///       <item>
///          <description>
///             HTTP: <c>McpServerConfig.Http("notion", new Uri("https://mcp.notion.com/sse"))</c>
///          </description>
///       </item>
///    </list>
/// </remarks>
public sealed record McpServerConfig
{
   /// <summary>Logical server name surfaced to participants (e.g., "web", "notion", "postgres").</summary>
   public required string Name { get; init; }

   /// <summary>Transport used to reach this server.</summary>
   public McpTransportType TransportType { get; init; } = McpTransportType.Stdio;

   // ── Stdio transport ──

   /// <summary>Executable command for stdio transport (e.g., "npx", "dotnet", "uvx").</summary>
   public string? Command { get; init; }

   /// <summary>Command-line arguments for stdio transport.</summary>
   public IReadOnlyList<string> Arguments { get; init; } = [];

   /// <summary>Working directory for the spawned stdio process.</summary>
   public string? WorkingDirectory { get; init; }

   /// <summary>Environment variables passed to the stdio process (e.g., API keys).</summary>
   public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
      new Dictionary<string, string>();

   // ── HTTP transport ──

   /// <summary>Endpoint URI for HTTP/SSE transport.</summary>
   public Uri? Endpoint { get; init; }

   /// <summary>Additional HTTP headers (e.g., authorization) for HTTP transport.</summary>
   public IReadOnlyDictionary<string, string> AdditionalHeaders { get; init; } =
      new Dictionary<string, string>();

   /// <summary>Creates a stdio MCP server configuration.</summary>
   public static McpServerConfig Stdio(
      string name,
      string command,
      IReadOnlyList<string>? arguments = null,
      IReadOnlyDictionary<string, string>? environmentVariables = null,
      string? workingDirectory = null) =>
      new()
      {
         Name = name,
         TransportType = McpTransportType.Stdio,
         Command = command,
         Arguments = arguments ?? [],
         EnvironmentVariables = environmentVariables ?? new Dictionary<string, string>(),
         WorkingDirectory = workingDirectory
      };

   /// <summary>Creates an HTTP/SSE MCP server configuration.</summary>
   public static McpServerConfig Http(
      string name,
      Uri endpoint,
      IReadOnlyDictionary<string, string>? additionalHeaders = null) =>
      new()
      {
         Name = name,
         TransportType = McpTransportType.Http,
         Endpoint = endpoint,
         AdditionalHeaders = additionalHeaders ?? new Dictionary<string, string>()
      };
}

/// <summary>
///    Describes a single tool exposed by an MCP server, as discovered by the Operator.
/// </summary>
/// <param name="ServerName">Logical name of the server that owns this tool.</param>
/// <param name="Name">Tool name (as defined by the MCP server).</param>
/// <param name="Description">Human-readable tool description.</param>
/// <param name="InputSchemaJson">JSON schema for the tool's input arguments (may be empty).</param>
public sealed record OperatorTool(
   string ServerName,
   string Name,
   string Description,
   string InputSchemaJson)
{
   /// <summary>Fully-qualified tool reference used when selecting tools (e.g., "web.search").</summary>
   public string QualifiedName => $"{ServerName}.{Name}";
}

/// <summary>
///    A single tool invocation performed by the Operator while fulfilling a request.
/// </summary>
/// <param name="ServerName">Server that owns the tool.</param>
/// <param name="ToolName">Name of the invoked tool.</param>
/// <param name="Arguments">Arguments passed to the tool.</param>
/// <param name="ResultText">Textual result returned by the tool.</param>
/// <param name="IsError">Whether the tool reported an error.</param>
public sealed record OperatorToolCall(
   string ServerName,
   string ToolName,
   IReadOnlyDictionary<string, object?> Arguments,
   string ResultText,
   bool IsError);

/// <summary>
///    Result of a single MCP tool call at the transport level.
/// </summary>
/// <param name="Text">Concatenated text content blocks returned by the tool.</param>
/// <param name="IsError">Whether the tool reported an error.</param>
public sealed record McpToolResult(string Text, bool IsError);

/// <summary>
///    Records one complete Operator interaction during a debate — a participant's task,
///    the tools used to fulfil it, and the Operator's synthesised answer.
/// </summary>
/// <param name="RequesterName">Display name of the participant who requested the Operator.</param>
/// <param name="Task">The natural-language task delegated to the Operator.</param>
/// <param name="Answer">The Operator's synthesised answer returned to the requester.</param>
/// <param name="ToolCalls">All MCP tool calls performed while fulfilling the task.</param>
/// <param name="Compressed">Whether the answer was compressed before being returned.</param>
/// <param name="Timestamp">UTC timestamp when the interaction completed.</param>
public sealed record OperatorInteraction(
   string RequesterName,
   string Task,
   string Answer,
   IReadOnlyList<OperatorToolCall> ToolCalls,
   bool Compressed,
   DateTime Timestamp)
{
   /// <summary>Number of tools invoked for this interaction.</summary>
   public int ToolCallCount => ToolCalls.Count;
}

/// <summary>
///    The Operator's response to a delegated task.
/// </summary>
/// <param name="RequesterName">Display name of the requester.</param>
/// <param name="Task">The original task.</param>
/// <param name="Answer">The synthesised answer.</param>
/// <param name="ToolCalls">Tool calls performed.</param>
/// <param name="Compressed">Whether the answer was compressed.</param>
public sealed record OperatorResult(
   string RequesterName,
   string Task,
   string Answer,
   IReadOnlyList<OperatorToolCall> ToolCalls,
   bool Compressed)
{
   /// <summary>Converts this result into a loggable <see cref="OperatorInteraction" />.</summary>
   public OperatorInteraction ToInteraction() =>
      new(RequesterName, Task, Answer, ToolCalls, Compressed, DateTime.UtcNow);
}
