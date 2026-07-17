namespace Delibera.Server.Api.Contracts;

/// <summary>
/// Fully self-describing council scenario. When submitted to POST /api/v1/scenarios
/// the server builds a CouncilBuilder from this JSON without referencing any
/// registered template. Useful for one-off councils, CI pipelines and SDK clients
/// that want total control over the council composition.
/// </summary>
public sealed record ScenarioRequest
{
   // ── Required ──────────────────────────────────────────────────────────────

   /// <summary>The central question / decision topic for the council.</summary>
   public required string Question { get; init; }

   /// <summary>Council members. At least one member is required.</summary>
   public required ScenarioMember[] Members { get; init; }

   // ── Optional council configuration ────────────────────────────────────────

   /// <summary>
   /// Debate strategy: Standard | Critique | Consensus.
   /// Defaults to "Standard".
   /// </summary>
   public string Strategy { get; init; } = "Standard";

   /// <summary>Optional chairman configuration. When omitted, no chairman is added.</summary>
   public ScenarioChairman? Chairman { get; init; }

   /// <summary>Voting engine: Majority | BordaCount | Weighted. Defaults to null (no voting).</summary>
   public string? VotingStrategy { get; init; }

   /// <summary>Per-member vote weights (used when VotingStrategy = "Weighted").</summary>
   public Dictionary<string, float>? MemberWeights { get; init; }

   /// <summary>Number of debate rounds. Defaults to 3.</summary>
   public int MaxRounds { get; init; } = 3;

   /// <summary>LLM temperature 0.0–1.0. Defaults to 0.7.</summary>
   public float Temperature { get; init; } = 0.7f;

   /// <summary>Optional system prompt prepended to all members.</summary>
   public string? SystemPrompt { get; init; }

   /// <summary>Optional knowledge / background text injected as inline RAG context.</summary>
   public string? KnowledgeText { get; init; }

   /// <summary>IDs of pre-indexed RAG corpora to attach.</summary>
   public string[]? CorpusIds { get; init; }

   /// <summary>
   /// JSON Schema string for structured output enforcement.
   /// When provided, the chairman (or final synthesis) is instructed to
   /// produce a verdict matching this schema.
   /// </summary>
   public string? OutputSchema { get; init; }

   /// <summary>Compression strategy: Hybrid | Semantic | Deduplication | Summarization | None.</summary>
   public string CompressionStrategy { get; init; } = "None";

   /// <summary>Free-form domain input data passed as extra context (JSON).</summary>
   public JsonElement? InputData { get; init; }

   /// <summary>Optional label stored on the DebateRecord for filtering.</summary>
   public string? Label { get; init; }
}

/// <summary>A single council member definition inside a <see cref="ScenarioRequest"/>.</summary>
public sealed record ScenarioMember
{
   /// <summary>Display role name, e.g. "Architect" or "Devil's Advocate".</summary>
   public required string Role { get; init; }

   /// <summary>
   /// LLM model identifier, e.g. "qwen2.5:7b" or "gpt-4o".
   /// Defaults to the server's configured strong model when omitted.
   /// </summary>
   public string? Model { get; init; }

   /// <summary>
   /// Provider type: Ollama | OpenAI | AzureOpenAI | Anthropic.
   /// Defaults to the server's configured default provider.
   /// </summary>
   public string? Provider { get; init; }

   /// <summary>Persona / system-prompt for this member.</summary>
   public string? Persona { get; init; }

   /// <summary>Vote weight when VotingStrategy = "Weighted". Defaults to 1.0.</summary>
   public float Weight { get; init; } = 1.0f;

   /// <summary>Member capabilities: Text | Vision | Both. Defaults to "Text".</summary>
   public string Capabilities { get; init; } = "Text";
}

/// <summary>Chairman configuration inside a <see cref="ScenarioRequest"/>.</summary>
public sealed record ScenarioChairman
{
   /// <summary>LLM model for the chairman. Defaults to strong model.</summary>
   public string? Model { get; init; }

   /// <summary>Provider type. Defaults to default provider.</summary>
   public string? Provider { get; init; }

   /// <summary>System prompt / synthesis instructions for the chairman.</summary>
   public string? SystemPrompt { get; init; }

   /// <summary>
   /// When true the chairman opens the debate with an orientation statement.
   /// Defaults to false.
   /// </summary>
   public bool OpeningStatement { get; init; } = false;
}
