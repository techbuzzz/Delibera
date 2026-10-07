using System.Text.Json;
using System.Text.RegularExpressions;

namespace Delibera.Core.Tools;

/// <summary>
///    Reads files from inside a configured base directory.
/// </summary>
/// <remarks>
///    <para>
///    <b>Security.</b> Every path is resolved against a root and the result is checked to still
///    be inside it, which rejects <c>../</c> traversal, rooted paths and, on Windows, alternate
///    data streams and 8.3 short names that could otherwise slip past a naive prefix check.
///    A member that could read <c>/etc/shadow</c> or the host key would turn a debate into an
///    exfiltration channel.
///    </para>
///    <para>
///    Reads are size-capped: a member asking for a multi-gigabyte file would otherwise turn a
///    tool call into an out-of-memory condition in the host process.
///    </para>
/// </remarks>
public sealed partial class FileSystemToolProvider : IToolProvider
{
   private readonly string _root;
   private readonly int _maxBytes;

   /// <summary>
   ///    Creates a provider rooted at <paramref name="rootDirectory" />.
   /// </summary>
   /// <param name="rootDirectory">The only directory tree readable by members.</param>
   /// <param name="maxBytes">Largest file that will be read in one call; defaults to 1 MiB.</param>
   /// <exception cref="ArgumentException">The root does not exist or is not a directory.</exception>
   public FileSystemToolProvider(string rootDirectory, int maxBytes = 1024 * 1024)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
      if (maxBytes <= 0)
         throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Max bytes must be positive.");

      _root = Path.GetFullPath(rootDirectory);
      if (!Directory.Exists(_root))
         throw new ArgumentException($"Root directory does not exist: {_root}", nameof(rootDirectory));

      _maxBytes = maxBytes;
   }

   /// <inheritdoc />
   public string Name => "filesystem";

   /// <inheritdoc />
   public ValueTask<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default)
   {
      IReadOnlyList<AIFunction> tools =
      [
         AIFunctionFactory.Create(
            async (string path) => await ReadAsync(path, ct).ConfigureAwait(false),
            new AIFunctionFactoryOptions
            {
               Name = "read_file",
               Description =
                  $"Reads a UTF-8 text file from the '{_root}' directory tree. Rejects paths that escape it.",
               SerializerOptions = ToolJson.Options
            })
      ];

      return ValueTask.FromResult(tools);
   }

   private async Task<string> ReadAsync(string path, CancellationToken ct)
   {
      var resolved = ResolveInsideRoot(path);
      var info = new FileInfo(resolved);

      if (!info.Exists) return $"[error] No such file: {path}";
      if (info.Length > _maxBytes)
         return $"[error] '{path}' is {info.Length:N0} bytes, over the {_maxBytes:N0}-byte limit. Read part of it instead.";

      var text = await File.ReadAllTextAsync(resolved, ct).ConfigureAwait(false);
      return text;
   }

   /// <summary>
   ///    Resolves a caller-supplied path and refuses anything that leaves the root.
   /// </summary>
   /// <exception cref="UnauthorizedAccessException">The resolved path escapes the root.</exception>
   private string ResolveInsideRoot(string path)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(path);

      var combined = Path.GetFullPath(Path.Combine(_root, path));

      // The trailing separator matters: without it, "/data-evil" would pass a naive
      // StartsWith("/data") check.
      var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
         ? _root
         : _root + Path.DirectorySeparatorChar;

      if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal)
         && !string.Equals(combined, _root, StringComparison.Ordinal))
      {
         throw new UnauthorizedAccessException(
            $"'{path}' resolves outside the permitted root. Only paths under {_root} may be read.");
      }

      return combined;
   }
}

/// <summary>
///    Performs HTTP GET requests against an allow-list of hosts.
/// </summary>
/// <remarks>
///    <b>Security.</b> Two restrictions, both non-negotiable for a tool a model can call:
///    only hosts on the allow-list are reachable, and plaintext HTTP is refused by default. A
///    model-supplied URL with no allow-list turns the host into an open proxy that a debate
///    can aim at internal services.
/// </remarks>
public sealed class HttpToolProvider : IToolProvider
{
   private readonly HttpClient _http;
   private readonly IReadOnlySet<string> _allowedHosts;
   private readonly bool _allowInsecureHttp;
   private readonly int _maxCharacters;

   /// <summary>
   ///    Creates a provider restricted to the supplied hosts.
   /// </summary>
   /// <param name="httpClient">Client used for requests.</param>
   /// <param name="allowedHosts">Hosts a member may reach. An empty set blocks every request.</param>
   /// <param name="allowInsecureHttp">Whether plain <c>http://</c> is permitted.</param>
   /// <param name="maxCharacters">Response text truncated to this length before returning.</param>
   public HttpToolProvider(
      HttpClient httpClient,
      IEnumerable<string> allowedHosts,
      bool allowInsecureHttp = false,
      int maxCharacters = 20_000)
   {
      ArgumentNullException.ThrowIfNull(httpClient);
      ArgumentNullException.ThrowIfNull(allowedHosts);

      _http = httpClient;
      _allowedHosts = allowedHosts
         .Where(h => !string.IsNullOrWhiteSpace(h))
         .Select(h => h.Trim().ToLowerInvariant())
         .ToHashSet(StringComparer.Ordinal);
      _allowInsecureHttp = allowInsecureHttp;
      _maxCharacters = maxCharacters;
   }

   /// <inheritdoc />
   public string Name => "http";

   /// <inheritdoc />
   public ValueTask<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default)
   {
      IReadOnlyList<AIFunction> tools =
      [
         AIFunctionFactory.Create(
            async (string url) => await FetchAsync(url, ct).ConfigureAwait(false),
            new AIFunctionFactoryOptions
            {
               Name = "http_get",
               Description =
                  $"Fetches a URL over HTTP GET. Only these hosts are reachable: {string.Join(", ", _allowedHosts)}. "
                  + (_allowInsecureHttp ? "Plain HTTP is permitted." : "Plain HTTP is refused; use HTTPS."),
               SerializerOptions = ToolJson.Options
            })
      ];

      return ValueTask.FromResult(tools);
   }

   private async Task<string> FetchAsync(string url, CancellationToken ct)
   {
      if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
         return $"[error] '{url}' is not an absolute URL.";

      if (!_allowedHosts.Contains(uri.Host.ToLowerInvariant()))
         return $"[error] Host '{uri.Host}' is not on the allow-list. Permitted: {string.Join(", ", _allowedHosts)}.";

      if (uri.Scheme != Uri.UriSchemeHttps && !_allowInsecureHttp)
         return $"[error] Plain '{uri.Scheme}' is refused. Use HTTPS.";

      try
      {
         using var response = await _http.GetAsync(uri, ct).ConfigureAwait(false);
         var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

         var status = $"[{(int)response.StatusCode} {response.ReasonPhrase}]";
         var text = body.Length > _maxCharacters
            ? body[.._maxCharacters] + $"… [truncated from {body.Length:N0} characters]"
            : body;

         return $"{status}\n{text}";
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (Exception ex)
      {
         return $"[error] Request failed: {ex.Message}";
      }
   }
}

/// <summary>
///    Exposes the tools of connected MCP servers as debate-callable tools.
/// </summary>
/// <remarks>
///    Reuses the Operator's existing MCP adapter and therefore its trust model: whatever the
///    Operator was allowed to reach, a member is allowed to reach. Granting members MCP access
///    is therefore an explicit decision by the host, not something this type widens.
/// </remarks>
public sealed class McpToolProvider : IToolProvider
{
   private readonly IMcpClient _client;

   /// <summary>Creates a provider over an MCP client.</summary>
   /// <param name="client">The connected MCP client.</param>
   public McpToolProvider(IMcpClient client)
      => _client = client ?? throw new ArgumentNullException(nameof(client));

   /// <inheritdoc />
   public string Name => "mcp";

   /// <inheritdoc />
   public ValueTask<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default)
   {
      IReadOnlyList<AIFunction> tools =
      [
         AIFunctionFactory.Create(
            async (string toolName, string argumentsJson) =>
            {
               var arguments = ParseArguments(argumentsJson);
               var result = await _client.CallToolAsync(toolName, arguments, ct).ConfigureAwait(false);
               return result.IsError ? $"[error] {result.Text}" : result.Text;
            },
            new AIFunctionFactoryOptions
            {
               Name = "mcp_call",
               Description =
                  "Invokes a tool on the connected MCP server. The first argument is the MCP tool name; "
                  + "the second is its arguments as a raw JSON object.",
               SerializerOptions = ToolJson.Options
            })
      ];

      return ValueTask.FromResult(tools);
   }

   /// <summary>
   ///   Turns the model's raw JSON argument object into the dictionary the MCP client expects.
   ///   A malformed payload becomes an empty argument set rather than an exception, because a
   ///   model emitting bad JSON is a routine event, not a fault in the host.
   /// </summary>
   private static IReadOnlyDictionary<string, object?> ParseArguments(string? argumentsJson)
   {
      if (string.IsNullOrWhiteSpace(argumentsJson)) return new Dictionary<string, object?>();

      try
      {
         return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            argumentsJson,
            ToolJson.Options) ?? new Dictionary<string, object?>();
      }
      catch (JsonException)
      {
         return new Dictionary<string, object?>();
      }
   }
}

/// <summary>
///    Shared serializer settings for tool schemas and arguments.
/// </summary>
/// <remarks>
///    A <see cref="JsonSerializerOptions" /> handed to <c>AIFunctionFactory</c> is made read-only
///    as part of building the descriptor, and .NET 10 refuses to freeze options that carry no
///    <c>TypeInfoResolver</c>. Reflection-based resolution is therefore set explicitly rather
///    than left to the caller's defaults.
/// </remarks>
internal static class ToolJson
{
   public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = false,
      TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
   };
}

/// <summary>
///    Extracts <c>[[TOOL: name {json}]]</c> requests from a member's response text.
/// </summary>
/// <remarks>
///    <para>
///    This is the fallback transport for providers that return plain strings and cannot carry
///    structured function-call traffic. It mirrors the Operator's existing
///    <c>[[OPERATOR: …]]</c> convention rather than inventing a second dialect.
///    </para>
///    <para>
///    Source-generated rather than <c>RegexOptions.Compiled</c>: the pattern is a literal, so the
///    generator emits it at build time and it stays AOT- and trim-safe.
///    </para>
/// </remarks>
public static partial class ToolCallParser
{
   /// <summary>
   ///   Builds the instructions appended to a system prompt when tools are available.
   /// </summary>
   /// <remarks>
   ///   This is the tool wire format for providers that return plain strings: the model is told
   ///   the marker, and <c>LLMProviderChatClient</c> parses the marker back out of the response
   ///   into a real <see cref="FunctionCallContent" />. That translation is what lets the standard
   ///   <c>FunctionInvokingChatClient</c> middleware drive the loop for every provider, instead of
   ///   this library maintaining a second copy of it.
   /// </remarks>
   /// <param name="tools">The tools the member may call.</param>
   /// <returns>A directive block, or an empty string when there are no tools.</returns>
   public static string BuildBriefing(IReadOnlyList<AITool> tools)
   {
      if (tools.Count == 0) return string.Empty;

      var sb = new StringBuilder();
      sb.AppendLine();
      sb.AppendLine("── TOOLS (available) ──");
      sb.AppendLine("You may call these tools while forming your answer:");
      foreach (var tool in tools)
      {
         var name = tool is AIFunction function ? function.Name : tool.Name;
         sb.AppendLine($"- `{name}` — {tool.Description}");
      }

      sb.AppendLine();
      sb.AppendLine("To call a tool, include a line with this exact marker:");
      sb.AppendLine("[[TOOL: <tool name> {\"argument\": \"value\"}]]");
      sb.AppendLine("For example: [[TOOL: http_get {\"url\": \"https://example.com/pricing\"}]]");
      sb.AppendLine("The result is given back to you before the next round. Call a tool only when you");
      sb.AppendLine("genuinely need external information; state your reasoning alongside the call.");
      return sb.ToString();
   }

   /// <summary>Overload for a caller that already has function-typed tools.</summary>
   /// <param name="tools">The tools the member may call.</param>
   /// <returns>A directive block, or an empty string when there are no tools.</returns>
   public static string BuildBriefing(IReadOnlyList<AIFunction> tools)
   {
      var asTools = new List<AITool>(tools.Count);
      foreach (var tool in tools) asTools.Add(tool);

      return BuildBriefing(asTools);
   }

   /// <summary>Parses exactly one marker, or returns <c>null</c> when the text is not one.</summary>
   /// <param name="markerText">Candidate marker text, including the brackets.</param>
   public static Request? TryParseSingle(string? markerText)
   {
      if (string.IsNullOrWhiteSpace(markerText)) return null;

      foreach (Match match in ToolRequestRegex().Matches(markerText))
      {
         var name = match.Groups["name"].Value.Trim();
         if (name.Length == 0) continue;

         return new Request(name, match.Groups["args"].Value.Trim(), match.Value);
      }

      return null;
   }

   /// <summary>Removes every tool marker from the text, keeping the surrounding prose.</summary>
   /// <param name="text">Text possibly containing markers.</param>
   /// <returns>The text with markers removed.</returns>
   public static string StripMarkers(string? text)
   {
      if (string.IsNullOrEmpty(text)) return string.Empty;
      if (!text.Contains("[[", StringComparison.Ordinal)) return text;

      return ToolRequestRegex().Replace(text, string.Empty);
   }

   /// <summary>
   ///    Parses a raw JSON argument object into the dictionary the tool binder expects.
   /// </summary>
   /// <remarks>
   ///    Malformed payloads yield an empty argument set rather than an exception. A model emitting
   ///    bad JSON is a routine event; taking the host down over it is not an acceptable trade.
   /// </remarks>
   /// <param name="argumentsJson">Raw JSON, possibly absent or malformed.</param>
   public static Dictionary<string, object?> TryParseArguments(string? argumentsJson)
   {
      if (string.IsNullOrWhiteSpace(argumentsJson)) return [];

      try
      {
         return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson, ToolJson.Options) ?? [];
      }
      catch (JsonException)
      {
         return [];
      }
   }

   /// <summary>
   ///   Renders an already-bound argument dictionary as JSON, for putting a tool call back into a
   ///   text prompt.
   /// </summary>
   /// <remarks>
   ///   The result is never null and never throws: this runs while flattening a conversation, and a
   ///   model-supplied payload that cannot be serialized must not cost the whole turn its tool
   ///   history.
   /// </remarks>
   /// <param name="arguments">The bound arguments, possibly null or empty.</param>
   public static string SerializeArguments(IDictionary<string, object?>? arguments)
   {
      if (arguments is null || arguments.Count == 0) return "{}";

      try
      {
         return JsonSerializer.Serialize(arguments, ToolJson.Options);
      }
      catch (JsonException)
      {
         return $"<{arguments.Count} argument(s), unserializable>";
      }
   }

   /// <summary>A tool request parsed out of a response.</summary>
   /// <param name="ToolName">Requested tool name.</param>
   /// <param name="ArgumentsJson">Raw JSON argument object.</param>
   /// <param name="Marker">The full marker text, so it can be stripped from the response.</param>
   public sealed record Request(string ToolName, string ArgumentsJson, string Marker);

   [GeneratedRegex(
      @"\[\[\s*TOOL\s*:\s*(?<name>[A-Za-z0-9_.\-]+)\s*(?<args>\{.*?\})?\s*\]\]",
      RegexOptions.Singleline | RegexOptions.IgnoreCase)]
   private static partial Regex ToolRequestRegex();

   /// <summary>Finds every tool request in a response, in the order the model emitted them.</summary>
   /// <param name="response">The member's raw response text.</param>
   /// <returns>The parsed requests; empty when the member called nothing.</returns>
   public static IReadOnlyList<Request> Parse(string? response)
   {
      if (string.IsNullOrWhiteSpace(response)) return [];

      // The literal "[[" is the same cheap pre-check the Operator path uses: without it the
      // engine has nothing to anchor on and a lazy Singleline pattern walks the whole response.
      if (!response.Contains("[[", StringComparison.Ordinal)) return [];

      var requests = new List<Request>();
      foreach (Match match in ToolRequestRegex().Matches(response))
      {
         var name = match.Groups["name"].Value.Trim();
         if (name.Length == 0) continue;

         requests.Add(new Request(name, match.Groups["args"].Value.Trim(), match.Value));
      }

      return requests;
   }
}