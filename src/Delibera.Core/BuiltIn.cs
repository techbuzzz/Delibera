using System.Text.Json;

namespace Delibera.Core;

/// <summary>
///    Built-in constants, presets and default values shared across all Delibera
///    projects. Eliminates duplicated magic strings and numbers that would
///    otherwise drift apart between Delibera.Core, Delibera.Redis, Delibera.Server
///    and consumer applications.
/// </summary>
/// <remarks>
///    <para>
///       Every constant here is a <c>const</c> (or <c>static readonly</c> for
///       non-primitive types) so the value is inlined at compile time — there is
///       no runtime cost and no allocation. Consumers reference the constant
///       directly (<c>BuiltIn.HttpHeaders.CorrelationId</c>) instead of copying
///       the literal into a private field.
///    </para>
///    <para>
///       Grouping is by domain: <see cref="HttpHeaders" />,
///       <see cref="SseEvents" />, <see cref="ConfigSections" />,
///       <see cref="Models" />, <see cref="Endpoints" />, <see cref="Timeouts" />,
///       <see cref="Redis" />, <see cref="HttpClients" />, <see cref="Roles" />,
///       <see cref="MetadataKeys" />, <see cref="Json" />, <see cref="Paths" />.
///    </para>
///    <para>
///       <b>SSE event names</b> (<see cref="SseEvents" />) are a contract with
///       the Web UI front-end. Changing a value here requires a matching change
///       in <c>Delibera.WebUI</c>.
///    </para>
/// </remarks>
public static class BuiltIn
{
   /// <summary>
   ///    Standard HTTP header names used across the server, middleware and
   ///    client code.
   /// </summary>
   public static class HttpHeaders
   {
      /// <summary>Correlation identifier header — <c>X-Correlation-Id</c>.</summary>
      public const string CorrelationId = "X-Correlation-Id";

      /// <summary>Tenant identifier header — <c>X-Tenant-Id</c>.</summary>
      public const string TenantId = "X-Tenant-Id";

      /// <summary>Nginx proxy buffering hint — <c>X-Accel-Buffering</c>.</summary>
      public const string AccelBuffering = "X-Accel-Buffering";
   }

   /// <summary>
   ///    Common HTTP header values (content types, cache directives, auth scheme).
   /// </summary>
   public static class HttpHeaderValues
   {
      /// <summary>Server-Sent Events content type — <c>text/event-stream</c>.</summary>
      public const string EventStream = "text/event-stream";

      /// <summary>Disable caching directive — <c>no-cache</c>.</summary>
      public const string NoCache = "no-cache";

      /// <summary>Negative value used for buffering / connection toggles — <c>no</c>.</summary>
      public const string No = "no";

      /// <summary>JSON content type — <c>application/json</c>.</summary>
      public const string ApplicationJson = "application/json";

      /// <summary>Bearer authentication scheme — <c>Bearer</c>.</summary>
      public const string Bearer = "Bearer";
   }

   /// <summary>
   ///    Server-Sent Events event type names. <b>Contract with Delibera.WebUI</b> —
   ///    the front-end listens for these exact strings; changing a value here
   ///    requires a matching change in the Web UI event map.
   /// </summary>
   public static class SseEvents
   {
      /// <summary>A debate round was produced — <c>debate-round</c>.</summary>
      public const string Round = "debate-round";

      /// <summary>The debate finished successfully — <c>debate-completed</c>.</summary>
      public const string Completed = "debate-completed";

      /// <summary>The debate failed — <c>debate-error</c>.</summary>
      public const string Error = "debate-error";

      /// <summary>The debate was cancelled — <c>debate-cancelled</c>.</summary>
      public const string Cancelled = "debate-cancelled";

      /// <summary>Keep-alive comment sent on idle — <c>keep-alive</c>.</summary>
      public const string KeepAlive = "keep-alive";

      /// <summary>Reconnect hint retry interval — <c>retry: 3000</c>.</summary>
      public const string Retry = "retry: 3000";
   }

   /// <summary>
   ///    Top-level configuration section names used in <c>appsettings.json</c>
   ///    and <see cref="Microsoft.Extensions.Configuration.IConfiguration" />.
   /// </summary>
   public static class ConfigSections
   {
      /// <summary>Root Delibera configuration section — <c>Delibera</c>.</summary>
      public const string Root = "Delibera";

      /// <summary>Redis orchestrator configuration section — <c>Delibera:Redis</c>.</summary>
      public const string Redis = "Delibera:Redis";

      /// <summary>Server configuration section — <c>Delibera:Server</c>.</summary>
      public const string Server = "Delibera:Server";
   }

   /// <summary>
   ///    Fully-qualified configuration keys (section + key path) used with
   ///    <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> indexer.
   /// </summary>
   public static class ConfigKeys
   {
      /// <summary>Default LLM provider endpoint — <c>Delibera:Providers:DefaultEndpoint</c>.</summary>
      public const string ProvidersDefaultEndpoint = "Delibera:Providers:DefaultEndpoint";

      /// <summary>Default LLM provider API key — <c>Delibera:Providers:ApiKey</c>.</summary>
      public const string ProvidersApiKey = "Delibera:Providers:ApiKey";

      /// <summary>Embedding model name — <c>Delibera:Providers:EmbeddingModel</c>.</summary>
      public const string ProvidersEmbeddingModel = "Delibera:Providers:EmbeddingModel";

      /// <summary>Fast/cheap model name — <c>Delibera:Models:Fast</c>.</summary>
      public const string ModelsFast = "Delibera:Models:Fast";

      /// <summary>Strong/expensive model name — <c>Delibera:Models:Strong</c>.</summary>
      public const string ModelsStrong = "Delibera:Models:Strong";

      /// <summary>RAG feature toggle — <c>Delibera:Rag:Enabled</c>.</summary>
      public const string RagEnabled = "Delibera:Rag:Enabled";

      /// <summary>RAG provider type — <c>Delibera:Rag:ProviderType</c>.</summary>
      public const string RagProviderType = "Delibera:Rag:ProviderType";
   }

   /// <summary>
   ///    Default LLM model identifiers used when no model is explicitly
   ///    configured. These are Ollama model tags.
   /// </summary>
   public static class Models
   {
      /// <summary>Default fast/cheap model — <c>llama3.2:3b</c>.</summary>
      public const string DefaultFast = "llama3.2:3b";

      /// <summary>Default strong/expensive model — <c>qwen2.5:7b</c>.</summary>
      public const string DefaultStrong = "qwen2.5:7b";

      /// <summary>Default embedding model — <c>nomic-embed-text</c>.</summary>
      public const string DefaultEmbedding = "nomic-embed-text";
   }

   /// <summary>
   ///    Default service endpoints for local and cloud LLM providers.
   /// </summary>
   public static class Endpoints
   {
      /// <summary>Local Ollama endpoint — <c>http://localhost:11434</c>.</summary>
      public const string OllamaLocal = "http://localhost:11434";

      /// <summary>Ollama cloud endpoint — <c>https://api.ollama.com</c>.</summary>
      public const string OllamaCloud = "https://api.ollama.com";
   }

   /// <summary>
   ///    Shared timeout values. Declared as <c>static readonly</c> because
   ///    <see cref="TimeSpan" /> is a struct and cannot be <c>const</c>.
   /// </summary>
   public static class Timeouts
   {
      /// <summary>
      ///    How long a completed debate record is retained in orchestrator caches
      ///    before eviction — 30 minutes.
      /// </summary>
      public static readonly TimeSpan CompletedDebateLifetime = TimeSpan.FromMinutes(30);

      /// <summary>
      ///    SSE heartbeat interval — 15 seconds.
      /// </summary>
      public static readonly TimeSpan SseHeartbeat = TimeSpan.FromSeconds(15);
   }

   /// <summary>
   ///    Redis key prefixes, stream names, consumer-group names and hash/stream
   ///    field names shared between <c>Delibera.Redis</c> and <c>Delibera.Server</c>.
   /// </summary>
   public static class Redis
   {
      /// <summary>Stream key for pending debate jobs — <c>delibera:jobs</c>.</summary>
      public const string JobStreamKey = "delibera:jobs";

      /// <summary>Stream key for debate round events — <c>delibera:events</c>.</summary>
      public const string EventStreamKey = "delibera:events";

      /// <summary>Key prefix for per-debate state hashes — <c>delibera:state:</c>.</summary>
      public const string StateKeyPrefix = "delibera:state:";

      /// <summary>Consumer group name for the orchestrator — <c>orchestrator</c>.</summary>
      public const string OrchestratorGroup = "orchestrator";

      /// <summary>Consumer group name for workers — <c>workers</c>.</summary>
      public const string WorkerGroup = "workers";

      /// <summary>
      ///    Hash field names and stream entry field names used in Redis payloads.
      /// </summary>
      public static class Fields
      {
         /// <summary>Debate status field — <c>status</c>.</summary>
         public const string Status = "status";

         /// <summary>Last-updated timestamp field — <c>updatedAt</c>.</summary>
         public const string UpdatedAt = "updatedAt";

         /// <summary>Debate result payload field — <c>result</c>.</summary>
         public const string Result = "result";

         /// <summary>Error message field — <c>errorMessage</c>.</summary>
         public const string ErrorMessage = "errorMessage";

         /// <summary>Debate identifier stream entry field — <c>debateId</c>.</summary>
         public const string DebateId = "debateId";

         /// <summary>Event type stream entry field — <c>eventType</c>.</summary>
         public const string EventType = "eventType";

         /// <summary>Event payload stream entry field — <c>payload</c>.</summary>
         public const string Payload = "payload";

         /// <summary>Job type stream entry field — <c>jobType</c>.</summary>
         public const string JobType = "jobType";

         /// <summary>Round-completed event type value — <c>round-completed</c>.</summary>
         public const string RoundCompleted = "round-completed";
      }
   }

   /// <summary>
   ///    Named <see cref="System.Net.Http.IHttpClientFactory" /> client identifiers
   ///    registered in DI.
   /// </summary>
   public static class HttpClients
   {
      /// <summary>Local Ollama HTTP client name — <c>Delibera.Ollama.Local</c>.</summary>
      public const string OllamaLocal = "Delibera.Ollama.Local";

      /// <summary>Cloud Ollama HTTP client name — <c>Delibera.Ollama.Cloud</c>.</summary>
      public const string OllamaCloud = "Delibera.Ollama.Cloud";

      /// <summary>YandexGPT HTTP client name — <c>Delibera.YandexGPT</c>.</summary>
      public const string YandexGpt = "Delibera.YandexGPT";
   }

   /// <summary>
   ///    Well-known council member role names.
   /// </summary>
   public static class Roles
   {
      /// <summary>Standard chairman role — <c>Chairman</c>.</summary>
      public const string Chairman = "Chairman";

      /// <summary>Voting chairman role — <c>Voting Chairman</c>.</summary>
      public const string VotingChairman = "Voting Chairman";

      /// <summary>Default expert member role — <c>Expert</c>.</summary>
      public const string Expert = "Expert";
   }

   /// <summary>
   ///    Metadata key names used in vector-store payloads, agent memory records
   ///    and council execution metadata dictionaries.
   /// </summary>
   public static class MetadataKeys
   {
      /// <summary>Agent name metadata key — <c>agent_name</c>.</summary>
      public const string AgentName = "agent_name";

      /// <summary>Debate identifier metadata key — <c>debate_id</c>.</summary>
      public const string DebateId = "debate_id";

      /// <summary>Member role metadata key — <c>role</c>.</summary>
      public const string Role = "role";

      /// <summary>Source file path metadata key — <c>source_path</c>.</summary>
      public const string SourcePath = "source_path";

      /// <summary>Source name metadata key — <c>source</c>.</summary>
      public const string Source = "source";

      /// <summary>Chunk text payload key — <c>text</c>.</summary>
      public const string Text = "text";

      /// <summary>Topic metadata key — <c>topic</c>.</summary>
      public const string Topic = "topic";
   }

   /// <summary>
   ///    Shared <see cref="JsonSerializerOptions" /> instances. Reusing a single
   ///    instance avoids per-call allocation and keeps serialisation behaviour
   ///    consistent across caches, SSE writers and persistence layers.
   /// </summary>
   public static class Json
   {
      /// <summary>
      ///    Default options: camelCase property naming, null values omitted.
      /// </summary>
      public static readonly JsonSerializerOptions Default = new()
      {
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
         DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
      };

      /// <summary>
      ///    Web-friendly options using <see cref="JsonSerializerDefaults.Web" />
      ///    (camelCase + case-insensitive reads).
      /// </summary>
      public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
   }

   /// <summary>
   ///    Well-known file-system directory and collection names.
   /// </summary>
   public static class Paths
   {
      /// <summary>Default directory for debate result exports — <c>debate_results</c>.</summary>
      public const string DebateResults = "debate_results";

      /// <summary>Default directory for debate checkpoints — <c>debate_checkpoints</c>.</summary>
      public const string DebateCheckpoints = "debate_checkpoints";

      /// <summary>Default vector-store collection for council knowledge — <c>council_knowledge</c>.</summary>
      public const string CouncilKnowledge = "council_knowledge";

      /// <summary>Agent memory collection/table name prefix — <c>agent_memory_</c>.</summary>
      public const string AgentMemoryPrefix = "agent_memory_";

      /// <summary>Agent memory default table name — <c>agent_memory</c>.</summary>
      public const string AgentMemoryTable = "agent_memory";
   }

   /// <summary>Redis consumer-group error marker — <c>BUSYGROUP</c>.</summary>
   public const string BusyGroup = "BUSYGROUP";

   /// <summary>CORS policy name — <c>DeliberaCors</c>.</summary>
   public const string CorsPolicyName = "DeliberaCors";

   /// <summary>MCP tenant identifier — <c>mcp</c>.</summary>
   public const string McpTenantId = "mcp";
}
