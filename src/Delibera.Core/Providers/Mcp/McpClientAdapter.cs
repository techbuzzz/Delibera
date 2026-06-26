using Delibera.Core.Resilience;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Delibera.Core.Providers.Mcp;

/// <summary>
///    Default <see cref="IMcpClient" /> implementation backed by the official
///    <c>ModelContextProtocol</c> C# SDK. Supports both stdio (local child process) and
///    HTTP/SSE (remote) MCP servers, selected via <see cref="McpServerConfig" />.
/// </summary>
/// <remarks>
///    <para>
///       When constructed through DI the adapter wires its HTTP transport into
///       the host's <see cref="IHttpClientFactory" /> (logical name
///       <c>"Delibera.Mcp.{ServerName}"</c>) so retries/circuit-breakers configured
///       via <c>Microsoft.Extensions.Http.Resilience</c> apply to MCP traffic.
///       When constructed standalone the transport owns its own
///       <see cref="HttpClient" /> without resilience.
///    </para>
/// </remarks>
public sealed class McpClientAdapter : IMcpClient
{
   private readonly McpServerConfig _config;
   private readonly IHttpClientFactory? _httpClientFactory;
   private readonly string? _httpClientName;
   private McpClient? _client;
   private bool _disposed;

   /// <summary>Creates a standalone adapter for the given MCP server configuration (no DI, no resilience).</summary>
   /// <param name="config">Server connection configuration.</param>
   public McpClientAdapter(McpServerConfig config)
      : this(config, httpClientFactory: null, httpClientName: null, loggerFactory: null)
   {
   }

   /// <summary>Creates an adapter that routes its HTTP transport through an <see cref="IHttpClientFactory" />.</summary>
   /// <param name="config">Server connection configuration.</param>
   /// <param name="httpClientFactory">Factory that produces the configured <see cref="HttpClient" />.</param>
   /// <param name="httpClientName">
   ///    Logical client name (default: <c>Delibera.Mcp.{ServerName}</c>).
   ///    The host must register the client with <c>AddHttpClient(name)</c> and any
   ///    resilience handlers attached via <c>AddResilienceHandler</c>.
   /// </param>
   /// <param name="loggerFactory">Optional logger factory passed to the MCP transport.</param>
   public McpClientAdapter(
      McpServerConfig config,
      IHttpClientFactory? httpClientFactory,
      string? httpClientName = null,
      ILoggerFactory? loggerFactory = null)
   {
      _config = config ?? throw new ArgumentNullException(nameof(config));
      ArgumentException.ThrowIfNullOrWhiteSpace(config.Name);
      _httpClientFactory = httpClientFactory;
      _httpClientName = string.IsNullOrWhiteSpace(httpClientName)
         ? $"Delibera.Mcp.{config.Name}"
         : httpClientName;
      LoggerFactory = loggerFactory;
   }

   private ILoggerFactory? LoggerFactory { get; }

   /// <inheritdoc />
   public string ServerName => _config.Name;

   /// <inheritdoc />
   public bool IsConnected => _client is not null;

   /// <inheritdoc />
   public async Task ConnectAsync(CancellationToken ct = default)
   {
      if (_client is not null) return;

      var transport = CreateTransport(_config, _httpClientFactory, _httpClientName, LoggerFactory);
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
            t.Description,
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

   private static IClientTransport CreateTransport(
      McpServerConfig config,
      IHttpClientFactory? factory,
      string? httpClientName,
      ILoggerFactory? loggerFactory)
   {
      return config.TransportType switch
      {
         McpTransportType.Stdio => CreateStdioTransport(config),
         McpTransportType.Http => CreateHttpTransport(config, factory, httpClientName, loggerFactory),
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

   private static HttpClientTransport CreateHttpTransport(
      McpServerConfig config,
      IHttpClientFactory? factory,
      string? httpClientName,
      ILoggerFactory? loggerFactory)
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

      if (factory is not null && !string.IsNullOrWhiteSpace(httpClientName))
      {
         // Inject the DI-managed HttpClient (with its resilience pipeline
         // attached via AddResilienceHandler). The transport disposes the
         // HttpClient on shutdown because ownsHttpClient=true.
         var httpClient = factory.CreateClient(httpClientName);
         return new HttpClientTransport(options, httpClient, loggerFactory, ownsHttpClient: true);
      }

      return loggerFactory is null
         ? new HttpClientTransport(options)
         : new HttpClientTransport(options, loggerFactory);
   }
}