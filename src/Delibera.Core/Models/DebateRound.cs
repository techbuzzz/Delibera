namespace Delibera.Core.Models;

/// <summary>
/// A single round of debate — contains every participant's response.
/// </summary>
public sealed record DebateRound
{
   /// <summary>Round number (1-based).</summary>
   public required int RoundNumber { get; init; }

   /// <summary>Round title (e.g., "Initial Responses", "Critique").</summary>
   public required string RoundName { get; init; }

   /// <summary>Optional description of the round's objective.</summary>
   public string? Description { get; init; }

   /// <summary>Participant responses keyed by display name.</summary>
   public required IReadOnlyDictionary<string, string> Responses { get; init; }

   /// <summary>The prompt used during this round.</summary>
   public string? RoundPrompt { get; init; }

   /// <summary>Knowledge Keeper queries &amp; answers that occurred during this round.</summary>
   public IReadOnlyList<KnowledgeInteraction> KnowledgeInteractions { get; init; } = [];

   /// <summary>Timestamp when the round started.</summary>
   public DateTime StartedAt { get; init; } = DateTime.UtcNow;

   /// <summary>Timestamp when the round completed.</summary>
   public DateTime? CompletedAt { get; init; }

   /// <summary>Round duration.</summary>
   public TimeSpan Duration => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
}

/// <summary>
/// Records a single interaction with the Knowledge Keeper during a debate round.
/// </summary>
/// <param name="Query">The query sent to the Knowledge Keeper.</param>
/// <param name="Answer">The answer returned by the Knowledge Keeper.</param>
/// <param name="SourceChunks">Number of RAG chunks used.</param>
public sealed record KnowledgeInteraction(string Query, string Answer, int SourceChunks);

/// <summary>
/// Structured context provided by the Knowledge Keeper for a specific debate round.
/// </summary>
/// <param name="RoundNumber">Round number this context was provided for.</param>
/// <param name="Answer">The Knowledge Keeper's formatted answer with structured sections.</param>
/// <param name="Sources">Ranked list of source chunks used.</param>
/// <param name="Interaction">The underlying interaction record.</param>
public sealed record KnowledgeRoundContext(
    int RoundNumber,
    string Answer,
    IReadOnlyList<KnowledgeSource> Sources,
    KnowledgeInteraction Interaction);

/// <summary>
/// A single source chunk from the Knowledge Keeper's knowledge base.
/// </summary>
/// <param name="Text">Source text content.</param>
/// <param name="Score">Relevance score (higher = more relevant).</param>
/// <param name="Metadata">Source metadata (file name, page, etc.).</param>
public sealed record KnowledgeSource(
    string Text,
    float Score,
    IReadOnlyDictionary<string, string> Metadata);
