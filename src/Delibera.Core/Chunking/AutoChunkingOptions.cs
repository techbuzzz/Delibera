namespace Delibera.Core.Chunking;

/// <summary>
///    Configuration options for the AutoChunking feature.
///    Controls how large documents are split and distributed across debate rounds.
/// </summary>
public sealed record AutoChunkingOptions
{
   /// <summary>
   ///    The chunking strategy to use when splitting documents.
   ///    Default is <see cref="ChunkingStrategy.SemanticBoundary" /> — respects
   ///    Markdown headers, paragraphs, and sentence boundaries.
   /// </summary>
   public ChunkingStrategy Strategy { get; init; } = ChunkingStrategy.SemanticBoundary;

   /// <summary>
   ///    Safety margin as a fraction of the context window (0.0–0.5).
   ///    Reserves this portion of the context window to account for token estimation
   ///    inaccuracy. Default 0.15 = 15% safety margin.
   /// </summary>
   public double SafetyMargin { get; init; } = 0.15;

   /// <summary>
   ///    Maximum number of chunks to include in a single round's prompt.
   ///    Default is 3. Higher values mean fewer rounds but larger per-round prompts.
   /// </summary>
   public int MaxChunksPerRound { get; init; } = 3;

   /// <summary>
   ///    When <c>true</c>, the Chairman receives a Map-Reduce summary of all chunks
   ///    instead of the raw chunks. This is essential when the total document is too
   ///    large even for the Chairman's context window.
   ///    Default is <c>true</c>.
   /// </summary>
   public bool EnableMapReduce { get; init; } = true;

   /// <summary>
   ///    When <c>true</c>, chunks are progressively disclosed across rounds —
   ///    each round reveals new portions of the document. When <c>false</c>,
   ///    all chunks are included in every round (may exceed context window).
   ///    Default is <c>true</c>.
   /// </summary>
   public bool EnableProgressiveDisclosure { get; init; } = true;

   /// <summary>
   ///    The default options used when none are explicitly provided.
   /// </summary>
   public static AutoChunkingOptions Default { get; } = new();
}
