using Delibera.Core.Compression;

namespace Delibera.Core.Chunking;

/// <summary>
///    Orchestrates the AutoChunking workflow — analyses model capabilities,
///    creates a chunking plan, and distributes chunks across debate rounds.
/// </summary>
/// <remarks>
///    <para>
///       The orchestrator is invoked by <see cref="Council.CouncilExecutor" /> before the
///       debate begins. It queries each model's context window (via
///       <see cref="Interfaces.ILLMProvider.GetModelCapabilitiesAsync" /> or the
///       <see cref="Models.ModelContextWindowRegistry" /> fallback), calculates the
///       available token budget per round, and creates a <see cref="ChunkingPlan" />
///       if the knowledge content exceeds the smallest context window.
///    </para>
///    <para>
///       During the debate, <see cref="GetRoundContext" /> is called by each strategy
///       to retrieve the appropriate chunks for the current round. Chunks are distributed
///       evenly across rounds with progressive disclosure — each round reveals new
///       portions of the document.
///    </para>
/// </remarks>
public sealed class AutoChunkingOrchestrator
{
   private readonly AutoChunkingOptions _options;
   private readonly ILogger? _logger;

   /// <summary>
   ///    Creates a new orchestrator with the specified options.
   /// </summary>
   /// <param name="options">Chunking configuration. Defaults to <see cref="AutoChunkingOptions.Default" />.</param>
   /// <param name="logger">Optional logger for diagnostic output.</param>
   public AutoChunkingOrchestrator(AutoChunkingOptions? options = null, ILogger? logger = null)
   {
      _options = options ?? AutoChunkingOptions.Default;
      _logger = logger;
   }

   /// <summary>
   ///    Analyses all council models and the knowledge content, then creates a
   ///    <see cref="ChunkingPlan" /> if the content exceeds any model's context window.
   ///    Returns an enriched <see cref="PromptContext" /> with the plan attached.
   /// </summary>
   /// <param name="originalContext">The original prompt context with knowledge content.</param>
   /// <param name="members">All council participants.</param>
   /// <param name="chairman">The Chairman (may be <c>null</c>).</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>
   ///    A new <see cref="PromptContext" /> with <see cref="PromptContext.ChunkingPlan" />
   ///    and <see cref="PromptContext.AutoChunkingEnabled" /> populated.
   /// </returns>
   public async Task<PromptContext> PrepareContextAsync(
      PromptContext originalContext,
      IReadOnlyList<CouncilMember> members,
      CouncilMember? chairman,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(originalContext);
      ArgumentNullException.ThrowIfNull(members);

      // Nothing to chunk if there's no knowledge content.
      if (string.IsNullOrWhiteSpace(originalContext.KnowledgeContent))
      {
         _logger?.LogDebug("AutoChunking: no knowledge content to chunk — skipping.");
         return originalContext with { AutoChunkingEnabled = false };
      }

      // Collect all models that will receive prompts.
      var allModels = members
         .Select(m => (m.ModelName, Provider: m.Provider, m.DisplayName))
         .ToList();

      if (chairman is not null)
         allModels.Add((chairman.ModelName, chairman.Provider, chairman.DisplayName));

      // Determine the minimum context window across all models.
      var (minWindow, minModel) = await DetermineMinContextWindowAsync(allModels, ct);

      if (minWindow is null)
      {
         _logger?.LogWarning(
            "AutoChunking: could not determine context window for any model. " +
            "Chunking disabled — prompts will be sent as-is.");
         return originalContext with { AutoChunkingEnabled = false };
      }

      // Calculate overhead: system prompt + user question + response buffer + history.
      var overheadTokens = CalculateOverhead(originalContext);

      _logger?.LogDebug(
         "AutoChunking: min context window = {MinWindow} tokens (model: {Model}), " +
         "overhead = {Overhead} tokens, available = {Available} tokens",
         minWindow.Value, minModel, overheadTokens, minWindow.Value - overheadTokens);

      // Create the chunking plan.
      var plan = AutoChunker.CreatePlan(
         originalContext.KnowledgeContent,
         minWindow.Value,
         overheadTokens,
         _options.Strategy,
         _options.SafetyMargin);

      if (plan.FitsInSingleRound)
      {
         _logger?.LogInformation(
            "AutoChunking: knowledge content ({TotalTokens} tokens) fits in a single round " +
            "({Available} tokens available). Chunking not needed.",
            plan.EstimatedTokensPerChunk * plan.TotalChunks,
            plan.AvailableTokensPerRound);

         return originalContext with
         {
            ChunkingPlan = plan,
            AutoChunkingEnabled = false,
            MinContextWindow = minWindow
         };
      }

      _logger?.LogInformation(
         "AutoChunking: knowledge content requires chunking — {TotalChunks} chunks, " +
         "~{TokensPerChunk} tokens/chunk, recommended {Rounds} rounds. " +
         "Smallest context window: {MinWindow} tokens ({Model}).",
         plan.TotalChunks, plan.EstimatedTokensPerChunk, plan.RecommendedRounds,
         minWindow.Value, minModel);

      return originalContext with
      {
         ChunkingPlan = plan,
         AutoChunkingEnabled = true,
         MinContextWindow = minWindow
      };
   }

   /// <summary>
   ///    Returns the chunked user prompt for a specific debate round.
   ///    When AutoChunking is disabled, falls back to <see cref="PromptContext.GetFullUserPrompt" />.
   /// </summary>
   /// <param name="context">The prompt context (must have a <see cref="PromptContext.ChunkingPlan" />).</param>
   /// <param name="roundNumber">Current round number (1-based).</param>
   /// <param name="totalRounds">Total number of rounds in the debate.</param>
   /// <param name="previousRounds">
   ///    Previous rounds for context continuity. When provided, a brief summary of
   ///    previously disclosed chunks is prepended so the model maintains context.
   /// </param>
   /// <returns>The formatted user prompt with the appropriate chunks for this round.</returns>
   public string GetRoundContext(
      PromptContext context,
      int roundNumber,
      int totalRounds,
      IReadOnlyList<DebateRound>? previousRounds = null)
   {
      ArgumentNullException.ThrowIfNull(context);

      if (!context.AutoChunkingEnabled || context.ChunkingPlan is not { } plan)
         return context.GetFullUserPrompt();

      var chunks = DistributeChunks(plan, roundNumber, totalRounds);

      if (chunks.Count == 0)
         return context.GetFullUserPrompt();

      // Build the chunked context block.
      var sb = new StringBuilder();

      // Add a summary of previously seen chunks for continuity.
      if (_options.EnableProgressiveDisclosure && roundNumber > 1 && previousRounds is { Count: > 0 })
      {
         sb.AppendLine("### Previously Reviewed (Summary):");
         sb.AppendLine($"(Rounds 1–{roundNumber - 1} covered chunks 1–" +
            $"{Math.Min((roundNumber - 1) * _options.MaxChunksPerRound, plan.TotalChunks)} " +
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
      sb.Append(context.UserPrompt);

      return sb.ToString();
   }

   /// <summary>
   ///    Returns the chunks assigned to a specific round, evenly distributing
   ///    all chunks across the total number of rounds.
   /// </summary>
   private IReadOnlyList<DocumentChunk> DistributeChunks(
      ChunkingPlan plan,
      int roundNumber,
      int totalRounds)
   {
      if (plan.TotalChunks == 0) return [];

      // Progressive disclosure: each round gets a slice of the chunks.
      if (_options.EnableProgressiveDisclosure)
      {
         var perRound = Math.Max(1, (int)Math.Ceiling((double)plan.TotalChunks / totalRounds));
         var start = (roundNumber - 1) * perRound;
         var end = Math.Min(start + perRound, plan.TotalChunks);

         if (start >= plan.TotalChunks) return []; // past the last chunk

         return plan.Chunks
            .Skip(start)
            .Take(Math.Min(end - start, _options.MaxChunksPerRound))
            .ToList()
            .AsReadOnly();
      }

      // Non-progressive: all chunks in every round (may exceed context window).
      return plan.Chunks
         .Take(_options.MaxChunksPerRound)
         .ToList()
         .AsReadOnly();
   }

   /// <summary>
   ///    Queries all models for their context window size and returns the minimum.
   ///    Falls back to <see cref="Models.ModelContextWindowRegistry" /> when a provider
   ///    cannot report capabilities dynamically.
   /// </summary>
   private static async Task<(int? MinWindow, string? ModelName)> DetermineMinContextWindowAsync(
      IReadOnlyList<(string ModelName, ILLMProvider Provider, string DisplayName)> models,
      CancellationToken ct)
   {
      int? minWindow = null;
      string? minModel = null;

      foreach (var (modelName, provider, displayName) in models)
      {
         int? window = null;

         // 1. Try the provider's dynamic capabilities.
         try
         {
            var caps = await provider.GetModelCapabilitiesAsync(modelName, ct);
            if (caps?.ContextWindowTokens is { } w and > 0)
               window = w;
         }
         catch (Exception ex)
         {
            // Provider introspection failed — fall through to registry.
            System.Diagnostics.Debug.WriteLine(
               $"AutoChunking: failed to get capabilities for {displayName}: {ex.Message}");
         }

         // 2. Fall back to the static registry.
         window ??= ModelContextWindowRegistry.GetContextWindow(modelName);

         if (window is { } w2 and > 0)
         {
            if (minWindow is null || w2 < minWindow.Value)
            {
               minWindow = w2;
               minModel = displayName;
            }
         }
      }

      return (minWindow, minModel);
   }

   /// <summary>
   ///    Estimates the token overhead per round — system prompt, user question,
   ///    response buffer, and debate history.
   /// </summary>
   private static int CalculateOverhead(PromptContext context)
   {
      var overhead = 0;

      // System prompt.
      overhead += TokenCounter.Default.EstimateTokens(context.SystemPrompt);

      // User question (without knowledge content).
      overhead += TokenCounter.Default.EstimateTokens(context.UserPrompt);

      // Response buffer — reserve space for the model's answer.
      overhead += 2000;

      // Debate history buffer — reserve space for previous rounds' responses.
      overhead += 3000;

      // Chunk metadata overhead (headers, markers).
      overhead += 200;

      return overhead;
   }
}
