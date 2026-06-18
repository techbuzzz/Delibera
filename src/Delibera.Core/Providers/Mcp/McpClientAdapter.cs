using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Delibera.Core.Providers.Mcp;

/// <summary>
///    Default <see cref="IMcpClient" /> implementation backed by the official
///    <c>ModelContextProtocol</c> C# SDK. Supports both stdio (local child process) and
///    HTTP/SSE (remote) MCP servers, selected via <see cref="McpServerConfig" />.
/// </summary>
public sealed class McpClientAdapter : IMcpClient
{
   private readonly McpServerConfig _config;
   private McpClient? _client;
   private bool _disposed;

   /// <summary>Creates an adapter for the given MCP server configuration.</summary>
   /// <param name="config">Server connection configuration.</param>
   public McpClientAdapter(McpServerConfig config)
   {
      _config = config ?? throw new ArgumentNullException(nameof(config));
      ArgumentException.ThrowIfNullOrWhiteSpace(config.Name);
   }

   /// <inheritdoc />
   public string ServerName => _config.Name;

   /// <inheritdoc />
   public bool IsConnected => _client is not null;

   /// <inheritdoc />
   public async Task ConnectAsync(CancellationToken ct = default)
   {
      if (_client is not null) return;

      var transport = CreateTransport(_config);
      _client = await McpClient.CreateAsync(transport, cancellationToken: ct);
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<OperatorTool>> ListToolsAsync(CancellationToken ct = default)
   {
      EnsureConnected();
      var tools = await _client!.ListToolsAsync(cancellationToken: ct);

      return tools
         .Select(t => new OperatorTool(
            ServerName,
            t.Name,
            t.Description ?? string.Empty,
            SchemaToJson(t)))
         .ToList()
         .AsReadOnly();
   }

   /// <inheritdoc />
   public async Task<McpToolResult> CallToolAsync(
      string toolName,
      IReadOnlyDictionary<string, object?> arguments,
      CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
      ArgumentNullException.ThrowIfNull(arguments);
      EnsureConnected();

      CallToolResult result;
      try
      {
         result = await _client!.CallToolAsync(toolName, arguments, cancellationToken: ct);
      }
      catch (Exception ex)
      {
         return new McpToolResult($"[MCP tool '{toolName}' failed: {ex.Message}]", true);
      }

      var text = string.Join(
         "\n",
         result.Content.OfType<TextContentBlock>().Select(b => b.Text));

      if (string.IsNullOrWhiteSpace(text))
         text = "[Tool returned no textual content]";

      return new McpToolResult(text, result.IsError ?? false);
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      if (_disposed) return;
      _disposed = true;

      if (_client is not null)
         await _client.DisposeAsync();
   }

   private void EnsureConnected()
   {
      if (_client is null)
         throw new InvalidOperationException(
            $"MCP client for server '{ServerName}' is not connected. Call ConnectAsync() first.");
   }

   private static string SchemaToJson(McpClientTool tool)
   {
      try
      {
         return tool.JsonSchema.GetRawText();
      }
      catch
      {
         return "{}";
      }
   }

   private static IClientTransport CreateTransport(McpServerConfig config)
   {
      return config.TransportType switch
      {
         McpTransportType.Stdio => CreateStdioTransport(config),
         McpTransportType.Http => CreateHttpTransport(config),
         _ => throw new NotSupportedException($"Unsupported MCP transport: {config.TransportType}")
      };
   }

   private static StdioClientTransport CreateStdioTransport(McpServerConfig config)
   {
      if (string.IsNullOrWhiteSpace(config.Command))
         throw new InvalidOperationException(
            $"MCP server '{config.Name}' uses stdio transport but no Command was provided.");

      var options = new StdioClientTransportOptions
      {
         Name = config.Name,
         Command = config.Command,
         Arguments = config.Arguments.ToList(),
         WorkingDirectory = config.WorkingDirectory
      };

      if (config.EnvironmentVariables.Count > 0)
         options.EnvironmentVariables = config.EnvironmentVariables
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);

      return new StdioClientTransport(options);
   }

   private static HttpClientTransport CreateHttpTransport(McpServerConfig config)
   {
      if (config.Endpoint is null)
         throw new InvalidOperationException(
            $"MCP server '{config.Name}' uses HTTP transport but no Endpoint was provided.");

      var options = new HttpClientTransportOptions
      {
         Name = config.Name,
         Endpoint = config.Endpoint
      };

      if (config.AdditionalHeaders.Count > 0)
         options.AdditionalHeaders = config.AdditionalHeaders
            .ToDictionary(kv => kv.Key, kv => kv.Value);

      return new HttpClientTransport(options);
   }
}