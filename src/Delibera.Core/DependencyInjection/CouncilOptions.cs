namespace Delibera.Core.DependencyInjection;

/// <summary>
/// Root configuration options for the Delibera framework.
/// Bind to a configuration section (e.g., "Delibera") via <c>IOptions&lt;CouncilOptions&gt;</c>.
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

   /// <summary>Provider configuration options.</summary>
   public ProviderOptions Providers { get; set; } = new();

   /// <summary>Compression configuration options.</summary>
   public CompressionConfig Compression { get; set; } = new();

   /// <summary>RAG configuration options.</summary>
   public RagOptions Rag { get; set; } = new();

   /// <summary>Output configuration options.</summary>
   public OutputOptions Output { get; set; } = new();
}

/// <summary>
/// Configuration options for LLM providers.
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
/// Configuration options for context compression.
/// </summary>
/// <remarks>
/// This is a DI-specific configuration class, distinct from <see cref="Interfaces.CompressionOptions"/>
/// which controls per-operation compression behaviour.
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
/// Configuration options for RAG (Retrieval-Augmented Generation).
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
/// Configuration options for debate output files.
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
