namespace Delibera.Core.DependencyInjection;

/// <summary>
///    Root configuration options for the Delibera framework.
///    Bind to a configuration section (e.g., "Delibera") via <c>IOptions&lt;CouncilOptions&gt;</c>.
/// </summary>
public sealed class CouncilOptions
{
   /// <summary>Configuration section name (default: "Delibera").</summary>
   public const string SectionName = "Delibera";

   /// <summary>Default debate strategy name (e.g., "Standard", "Critique", "Consensus").</summary>
   public string Strategy { get; set; } = "Standard";

   /// <summary>Maximum number of debate rounds (1–10).</summary>
   public int MaxRounds { get; set; } = 4;

   /// <summary>Default generation temperature (0.0–2.0).</summary>
   public float Temperature { get; set; } = 0.7f;

   /// <summary>Default system prompt for all participants.</summary>
   public string SystemPrompt { get; set; } = "You are a helpful AI assistant participating in a council debate.";

   /// <summary>
   ///    Forces every model response (participants, Chairman, Knowledge Keeper, Operator)
   ///    to be written in the specified human language, regardless of the language used
   ///    in the prompt or retrieved RAG context.
   /// </summary>
   /// <remarks>
   ///    <para>
   ///       Set to a language name the model recognises (e.g. "Russian", "English",
   ///       "Spanish", "中文"). When non-empty, Delibera injects a strict directive into
   ///       every system and user prompt:
   ///       <c>
   ///          "You MUST answer exclusively in {language}.
   ///          Never use any other language."
   ///       </c>
   ///    </para>
   ///    <para>
   ///       Leave <c>null</c> or empty to let the model pick a language from context (legacy
   ///       behaviour).
   ///    </para>
   /// </remarks>
   public string? ResponseLanguage { get; set; }

   /// <summary>
   ///    Optional maximum degree of parallelism for operations that can run concurrently
   ///    within a debate round (e.g. Operator task delegation, parallel Knowledge Keeper
   ///    queries). <c>0</c> means "unbounded" (default).
   /// </summary>
   public int MaxDegreeOfParallelism { get; set; }

   /// <summary>Provider configuration options.</summary>
   public ProviderOptions Providers { get; set; } = new();

   /// <summary>Compression configuration options.</summary>
   public CompressionConfig Compression { get; set; } = new();

   /// <summary>RAG configuration options.</summary>
   public RagOptions Rag { get; set; } = new();

   /// <summary>Operator (MCP tool micro-agent) configuration options.</summary>
   public OperatorConfig Operator { get; set; } = new();

   /// <summary>Output configuration options.</summary>
   public OutputOptions Output { get; set; } = new();

   /// <summary>
   ///    Polly v8 resilience options applied to every HTTP-backed provider
   ///    (Ollama, YandexGPT, MCP HTTP transport) through the named pipelines
   ///    registered by <c>AddDelibera</c>.
   /// </summary>
   public ResilienceOptions Resilience { get; set; } = new();
}

/// <summary>
///    Configuration options for the Polly v8 resilience pipelines consumed by
///    Delibera's HTTP-backed providers. Bound from the
///    <c>Delibera:Resilience</c> configuration section.
/// </summary>
/// <remarks>
///    <para>
///       Delibera registers three named pipelines out of the box:
///       <c>"Delibera.Local"</c>, <c>"Delibera.Cloud"</c>, and
///       <c>"Delibera.Default"</c>. Each one is a Polly v8
///       <c>ResiliencePipeline&lt;HttpResponseMessage&gt;</c> built from
///       <c>HttpRetryStrategyOptions</c>.
///       The Local pipeline retries only connection-level failures (no status
///       code); the Cloud pipeline retries transient HTTP responses (429, 524,
///       5xx) plus timeouts; the Default pipeline is an alias for whichever
///       of the two is more permissive.
///    </para>
///    <para>
///       Register custom pipelines with
///       <c>services.AddDeliberaResiliencePipeline("MyKey", builder =&gt; ...)</c>
///       and reference them from a provider by passing the same name to its
///       constructor.
///    </para>
/// </remarks>
public sealed class ResilienceOptions
{
   /// <summary>Default pipeline name used when a provider is constructed without an explicit name.</summary>
   public const string DefaultPipelineName = "Delibera.Default";

   /// <summary>Pipeline name used for Ollama-local / direct-on-host endpoints.</summary>
   public const string LocalPipelineName = "Delibera.Local";

   /// <summary>Pipeline name used for cloud-hosted LLM gateways (Ollama Cloud, Yandex Cloud, MCP HTTP).</summary>
   public const string CloudPipelineName = "Delibera.Cloud";

   /// <summary>Master switch — when <c>false</c> no retry pipeline is attached and HttpClients behave as plain <see cref="HttpClient" />.</summary>
   public bool Enabled { get; set; } = true;

   /// <summary>Maximum number of retry attempts (the initial call counts as the first attempt).</summary>
   public int MaxRetryAttempts { get; set; } = 3;

   /// <summary>Base delay used by the exponential back-off generator.</summary>
   public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

   /// <summary>Upper bound on the back-off delay produced by the exponential generator.</summary>
   public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

   /// <summary>Whether to apply random jitter to the back-off delay.</summary>
   public bool UseJitter { get; set; } = true;

   /// <summary>
   ///    Back-off style — either <c>"Exponential"</c> (default),
   ///    <c>"Linear"</c>, or <c>"Constant"</c>. Case-insensitive.
   /// </summary>
   public string BackoffType { get; set; } = "Exponential";

   /// <summary>
   ///    HTTP status codes (e.g. <c>429</c>, <c>500</c>, <c>524</c>) that should be retried on the
   ///    cloud pipeline. The local pipeline always retries only when the request has no status code
   ///    (i.e. connection-level failure). Defaults to <c>{ 408, 429, 500, 502, 503, 504, 524 }</c>.
   /// </summary>
   public int[] RetryableStatusCodes { get; set; } = [408, 429, 500, 502, 503, 504, 524];

   /// <summary>
   ///    Per-attempt timeout applied inside the pipeline (in addition to the outer HttpClient
   ///    timeout). Set to <see cref="Timeout.InfiniteTimeSpan" /> (or <c>TimeSpan.Zero</c>) to disable.
   /// </summary>
   public TimeSpan AttemptTimeout { get; set; } = TimeSpan.Zero;
}

/// <summary>
///    Configuration options for LLM providers.
/// </summary>
public sealed class ProviderOptions
{
   /// <summary>Default provider type (e.g., "Ollama").</summary>
   public string DefaultType { get; set; } = "Ollama";

   /// <summary>Default provider endpoint URL.</summary>
   public string DefaultEndpoint { get; set; } = "http://localhost:11434";

   /// <summary>API key for the default provider (if required).</summary>
   public string? ApiKey { get; set; }

   /// <summary>Embedding model name for RAG and semantic operations.</summary>
   public string EmbeddingModel { get; set; } = "llama2";
}

/// <summary>
///    Configuration options for context compression.
/// </summary>
/// <remarks>
///    This is a DI-specific configuration class, distinct from <see cref="Interfaces.CompressionOptions" />
///    which controls per-operation compression behaviour.
/// </remarks>
public sealed class CompressionConfig
{
   /// <summary>Whether compression is enabled by default.</summary>
   public bool Enabled { get; set; }

   /// <summary>Default compression strategy name (e.g., "Hybrid", "Semantic", "Deduplication").</summary>
   public string Strategy { get; set; } = "Deduplication";

   /// <summary>Default target compression ratio (0.1–1.0).</summary>
   public double TargetRatio { get; set; } = 0.5;

   /// <summary>Whether to enable compression caching.</summary>
   public bool EnableCache { get; set; } = true;

   /// <summary>Maximum number of cache entries.</summary>
   public int MaxCacheEntries { get; set; } = 256;
}

/// <summary>
///    Configuration options for RAG (Retrieval-Augmented Generation).
/// </summary>
public sealed class RagOptions
{
   /// <summary>Whether RAG is enabled.</summary>
   public bool Enabled { get; set; }

   /// <summary>RAG provider type (e.g., "Qdrant", "PgVector").</summary>
   public string ProviderType { get; set; } = "Qdrant";

   /// <summary>Vector database host.</summary>
   public string Host { get; set; } = "localhost";

   /// <summary>Vector database port.</summary>
   public int Port { get; set; } = 6334;

   /// <summary>Collection name for vector storage.</summary>
   public string CollectionName { get; set; } = "council_knowledge";

   /// <summary>Connection string (for PgVector).</summary>
   public string? ConnectionString { get; set; }
}

/// <summary>
///    Configuration options for the Operator (MCP tool micro-agent).
/// </summary>
/// <remarks>
///    Bind this from configuration (e.g., <c>Delibera:Operator</c>) and use
///    <see cref="ToServerConfigs" /> to materialise <see cref="Models.McpServerConfig" /> instances
///    for <c>ICouncilBuilder.WithOperator(...)</c>.
/// </remarks>
public sealed class OperatorConfig
{
   /// <summary>Whether the Operator is enabled.</summary>
   public bool Enabled { get; set; }

   /// <summary>Model name used by the Operator (typically a cheaper model than the participants).</summary>
   public string ModelName { get; set; } = string.Empty;

   /// <summary>Whether the Operator reuses the council's compressor for large tool results.</summary>
   public bool ReuseCompression { get; set; } = true;

   /// <summary>MCP servers the Operator connects to.</summary>
   public List<McpServerOptions> McpServers { get; set; } = [];

   /// <summary>Materialises the configured MCP servers into <see cref="Models.McpServerConfig" /> instances.</summary>
   public IReadOnlyList<McpServerConfig> ToServerConfigs()
   {
      return McpServers.Select(s => s.ToConfig()).ToList();
   }
}

/// <summary>
///    Configuration for a single MCP server bound from configuration.
/// </summary>
public sealed class McpServerOptions
{
   /// <summary>Logical server name surfaced to participants (e.g., "web", "notion", "postgres").</summary>
   public string Name { get; set; } = string.Empty;

   /// <summary>Transport type: "Stdio" (default) or "Http".</summary>
   public string Transport { get; set; } = "Stdio";

   // ── Stdio ──

   /// <summary>Executable command for stdio transport (e.g., "npx", "uvx", "dotnet").</summary>
   public string? Command { get; set; }

   /// <summary>Command-line arguments for stdio transport.</summary>
   public List<string> Arguments { get; set; } = [];

   /// <summary>Working directory for the spawned stdio process.</summary>
   public string? WorkingDirectory { get; set; }

   /// <summary>Environment variables passed to the stdio process.</summary>
   public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

   // ── Http ──

   /// <summary>Endpoint URL for HTTP/SSE transport.</summary>
   public string? Endpoint { get; set; }

   /// <summary>Additional HTTP headers (e.g., authorization) for HTTP transport.</summary>
   public Dictionary<string, string> AdditionalHeaders { get; set; } = [];

   /// <summary>Converts these options into a <see cref="Models.McpServerConfig" />.</summary>
   public McpServerConfig ToConfig()
   {
      return string.Equals(Transport, "Http", StringComparison.OrdinalIgnoreCase)
         ? McpServerConfig.Http(
            Name,
            new Uri(Endpoint ?? throw new InvalidOperationException($"MCP server '{Name}' uses Http transport but has no Endpoint.")),
            AdditionalHeaders)
         : McpServerConfig.Stdio(
            Name,
            Command ?? throw new InvalidOperationException($"MCP server '{Name}' uses Stdio transport but has no Command."),
            Arguments,
            EnvironmentVariables,
            WorkingDirectory);
   }
}

/// <summary>
///    Configuration options for debate output files.
/// </summary>
public sealed class OutputOptions
{
   /// <summary>Base directory for output files.</summary>
   public string Directory { get; set; } = "./debate_results";

   /// <summary>Whether to save separate files (result.md, statistics.md, logs.md) instead of a single file.</summary>
   public bool SeparateFiles { get; set; }

   /// <summary>Optional file prefix (default: "debate_{timestamp}").</summary>
   public string? FilePrefix { get; set; }
}