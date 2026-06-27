using Delibera.Core.Chunking;

namespace Delibera.Core.Models;

/// <summary>
///    Immutable context that feeds into a debate session.
/// </summary>
/// <param name="SystemPrompt">System prompt (role / context shared by all models).</param>
/// <param name="UserPrompt">User prompt (the main question or task).</param>
/// <param name="KnowledgeContent">Pre-loaded knowledge content (merged text from files or RAG).</param>
/// <param name="KnowledgeFiles">Paths / identifiers of knowledge sources.</param>
/// <param name="Metadata">Arbitrary key-value metadata.</param>
public sealed record PromptContext(
   string SystemPrompt = "",
   string UserPrompt = "",
   string? KnowledgeContent = null,
   IReadOnlyList<string> KnowledgeFiles = null!,
   IReadOnlyDictionary<string, string> Metadata = null!)
{
   /// <summary>
   ///    Creates an empty <see cref="PromptContext" /> with default collections.
   /// </summary>
   public PromptContext() : this(string.Empty, string.Empty, null, [], new Dictionary<string, string>())
   {
   }

   // ── AutoChunking fields ──

   /// <summary>
   ///    The chunking plan created by <see cref="AutoChunkingOrchestrator" />.
   ///    <c>null</c> when AutoChunking is disabled or no knowledge content is present.
   /// </summary>
   public ChunkingPlan? ChunkingPlan { get; init; }

   /// <summary>
   ///    <c>true</c> when the knowledge content exceeds the smallest model's context window
   ///    and must be split into chunks across rounds.
   /// </summary>
   public bool AutoChunkingEnabled { get; init; }

   /// <summary>
   ///    The smallest context window (in tokens) among all council models.
   ///    <c>null</c> when unknown. Used for diagnostics and logging.
   /// </summary>
   public int? MinContextWindow { get; init; }

   // ── Prompt builders ──

   /// <summary>
   ///    Builds the full user prompt, injecting knowledge context when available.
   ///    When AutoChunking is enabled, use <see cref="GetChunkedUserPrompt" /> instead
   ///    to get the round-appropriate chunks.
   /// </summary>
   public string GetFullUserPrompt()
   {
      return string.IsNullOrWhiteSpace(KnowledgeContent)
         ? UserPrompt
         : $"""
            ### Context (Knowledge Base):
            {KnowledgeContent}

            ### Question:
            {UserPrompt}
            """;
   }

   /// <summary>
   ///    Builds a chunked user prompt for a specific debate round.
   ///    When AutoChunking is disabled, falls back to <see cref="GetFullUserPrompt" />.
   /// </summary>
   /// <param name="roundNumber">Current round number (1-based).</param>
   /// <param name="totalRounds">Total number of rounds in the debate.</param>
   /// <param name="previousRounds">
   ///    Previous rounds for context continuity. When provided, a summary of previously
   ///    disclosed chunks is prepended.
   /// </param>
   /// <returns>The formatted user prompt with the appropriate chunks for this round.</returns>
   public string GetChunkedUserPrompt(
      int roundNumber,
      int totalRounds,
      IReadOnlyList<DebateRound>? previousRounds = null)
   {
      if (!AutoChunkingEnabled || ChunkingPlan is not { } plan)
         return GetFullUserPrompt();

      // Distribute chunks evenly across rounds.
      var perRound = Math.Max(1, (int)Math.Ceiling((double)plan.TotalChunks / totalRounds));
      var start = (roundNumber - 1) * perRound;
      var end = Math.Min(start + perRound, plan.TotalChunks);

      if (start >= plan.TotalChunks)
         return GetFullUserPrompt(); // past the last chunk — fall back

      var chunks = plan.Chunks
         .Skip(start)
         .Take(end - start)
         .ToList();

      if (chunks.Count == 0)
         return GetFullUserPrompt();

      var sb = new StringBuilder();

      // Summary of previously seen chunks for continuity.
      if (roundNumber > 1 && previousRounds is { Count: > 0 })
      {
         sb.AppendLine("### Previously Reviewed (Summary):");
         sb.AppendLine($"(Rounds 1–{roundNumber - 1} covered chunks 1–" +
            $"{Math.Min((roundNumber - 1) * perRound, plan.TotalChunks)} " +
            $"of {plan.TotalChunks})");
         sb.AppendLine();
      }

      sb.AppendLine($"### Context (Knowledge Base) — Part {roundNumber}/{totalRounds}:");
      sb.AppendLine($"(Chunks {chunks[0].Index + 1}–{chunks[^1].Index + 1} of {plan.TotalChunks})");
      sb.AppendLine();

      foreach (var chunk in chunks)
      {
         sb.AppendLine($"#### [Chunk {chunk.Index + 1}/{plan.TotalChunks}] {chunk.SectionTitle}");
         sb.AppendLine(chunk.Content);
         sb.AppendLine();
      }

      sb.AppendLine("### Question:");
      sb.Append(UserPrompt);

      return sb.ToString();
   }
}
