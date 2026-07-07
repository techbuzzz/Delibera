namespace Delibera.Server.Api.Contracts;

/// <summary>Payload for POST /api/v1/debates.</summary>
public sealed record CreateDebateRequest
{
    /// <summary>ID of the registered council template (e.g. "risk-committee").</summary>
    public required string TemplateId { get; init; }

    /// <summary>The central question / decision topic for the council.</summary>
    public required string Question { get; init; }

    /// <summary>Domain-specific input data (free-form JSON, passed to template context).</summary>
    public JsonElement? InputData { get; init; }

    /// <summary>IDs of pre-indexed RAG corpora to attach to the Knowledge Keeper.</summary>
    public string[]? CorpusIds { get; init; }

    /// <summary>Optional per-request overrides for debate execution.</summary>
    public DebateOptionsOverride? Options { get; init; }
}

public sealed record DebateOptionsOverride
{
    public int?    MaxRounds           { get; init; }
    public float?  Temperature         { get; init; }
    public string? CompressionStrategy { get; init; }   // Hybrid | Semantic | Deduplication | Summarization | None
    public string? VotingStrategy      { get; init; }   // Majority | BordaCount | Weighted
    public bool?   StreamingEnabled    { get; init; }
    public Dictionary<string, float>? MemberWeights { get; init; }
}
