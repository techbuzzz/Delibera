using Delibera.Core.Compression;

namespace Delibera.Core.Council;

/// <summary>
///    Executes a configured council debate session.
///    Created via <see cref="CouncilBuilder.Build" />.
/// </summary>
public sealed class CouncilExecutor : ICouncilExecutor
{
   private readonly CompressionOptions? _compressionOptions;
   private readonly PromptContext _context;
   private readonly List<ExecutionLog> _executionLogs = [];
   private readonly int _maxRounds;
   private readonly string? _outputPath;
   private readonly float _temperature;

   internal CouncilExecutor(
      IReadOnlyList<CouncilMember> members,
      CouncilMember? chairman,
      KnowledgeKeeper? knowledgeKeeper,
      IDebateStrategy strategy,
      PromptContext context,
      int maxRounds,
      float temperature,
      string? outputPath,
      IContextCompressor? compressor = null,
      CompressionOptions? compressionOptions = null,
      CompressionCache? compressionCache = null,
      Operator? @operator = null,
      DebateExecutionOptions? executionOptions = null)
   {
      Members = members;
      Chairman = chairman;
      KnowledgeKeeper = knowledgeKeeper;
      Operator = @operator;
      Strategy = strategy;
      _context = context;
      _maxRounds = maxRounds;
      _temperature = temperature;
      _outputPath = outputPath;
      Compressor = compressor;
      _compressionOptions = compressionOptions;
      CompressionCache = compressionCache;
      ExecutionOptions = executionOptions ?? DebateExecutionOptions.Default;
   }

   /// <summary>Compression cache (may be <c>null</c>).</summary>
   public CompressionCache? CompressionCache { get; }

   /// <summary>
   ///    Per-execution options (response language, parallelism budget, logger).
   ///    Populated from <see cref="CouncilBuilder" />.
   /// </summary>
   public DebateExecutionOptions ExecutionOptions { get; }

   /// <summary>Council participants.</summary>
   public IReadOnlyList<CouncilMember> Members { get; }

   /// <summary>Chairman (may be <c>null</c>).</summary>
   public CouncilMember? Chairman { get; }

   /// <summary>Knowledge Keeper (may be <c>null</c>).</summary>
   public KnowledgeKeeper? KnowledgeKeeper { get; }

   /// <summary>Operator (may be <c>null</c>).</summary>
   public Operator? Operator { get; }

   /// <summary>Debate strategy.</summary>
   public IDebateStrategy Strategy { get; }

   /// <summary>Context compressor (may be <c>null</c> if compression is disabled).</summary>
   public IContextCompressor? Compressor { get; }

   /// <inheritdoc />
   public ILogger? Logger => ExecutionOptions.Logger;

   /// <inheritdoc />
   public IReadOnlyList<ExecutionLog> ExecutionLogs => _executionLogs.AsReadOnly();

   /// <summary>Invoked after each round completes.</summary>
   public event Action<DebateRound>? OnRoundCompleted;

   /// <inheritdoc />
   public event Action<ExecutionLog>? OnLog;

   /// <inheritdoc />
   public event Action<Exception, string>? OnError;

   /// <summary>
   ///    Runs the debate and returns the full result.
   /// </summary>
   public async Task<DebateResult> ExecuteAsync(CancellationToken ct = default)
   {
      _executionLogs.Clear();

      Log(ExecutionLog.Info("Council", $"Starting debate — strategy: {Strategy.StrategyName}, members: {Members.Count}, maxRounds: {_maxRounds}"));

      if (ExecutionOptions.HasResponseLanguage)
         Log(ExecutionLog.Info("Council", $"Response language enforced: {ExecutionOptions.ResponseLanguage}"));

      if (ExecutionOptions.MaxDegreeOfParallelism > 0)
         Log(ExecutionLog.Info("Council", $"Parallelism cap: {ExecutionOptions.MaxDegreeOfParallelism}"));

      if (Chairman is not null)
         Log(ExecutionLog.Info("Chairman", $"Chairman assigned: {Chairman.DisplayName}"));

      if (KnowledgeKeeper is not null)
         Log(ExecutionLog.Info("KnowledgeKeeper", $"Knowledge Keeper ready: {KnowledgeKeeper.DisplayName} (collection: {KnowledgeKeeper.CollectionName})"));

      // Initialise the Operator (connect to MCP servers, discover tools) before the debate begins.
      if (Operator is not null)
      {
         if (!Operator.IsInitialized)
         {
            Log(ExecutionLog.Info("Operator", $"Initialising Operator: {Operator.DisplayName}…"));
            try
            {
               await Operator.InitializeAsync(ct);
            }
            catch (Exception ex)
            {
               ReportError(ex, "Operator");
            }
         }

         Log(ExecutionLog.Info("Operator", $"Operator ready: {Operator.DisplayName} ({Operator.AvailableTools.Count} tool(s) available)"));
      }

      if (Compressor is not null)
         Log(ExecutionLog.Info("Compression", $"Compression enabled: {Compressor.StrategyName}"));

      foreach (var m in Members)
         Log(ExecutionLog.Trace("Council", $"Participant registered: {m.DisplayName} [{m.Role}]"));

      // Inject the response-language directive into the system prompt so every downstream
      // call (participants, Chairman.OpenDebateAsync / SynthesizeVerdictAsync, Knowledge Keeper,
      // Operator) inherits it.
      var effectiveContext = ExecutionOptions.HasResponseLanguage
         ? _context with { SystemPrompt = _context.SystemPrompt + ExecutionOptions.BuildLanguageDirective() }
         : _context;

      var result = await Strategy.ExecuteAsync(
         Members,
         effectiveContext,
         Chairman,
         KnowledgeKeeper,
         Operator,
         ExecutionOptions,
         _maxRounds,
         _temperature,
         round =>
         {
            Log(ExecutionLog.Info("Council", $"Round {round.RoundNumber} completed: {round.RoundName} ({round.Duration.TotalSeconds:F1}s, {round.Responses.Count} responses)"));

            // Log knowledge interactions
            foreach (var ki in round.KnowledgeInteractions)
               Log(ExecutionLog.Info("KnowledgeKeeper", $"Query: \"{Truncate(ki.Query, 100)}\" → {ki.SourceChunks} chunks"));

            // Log operator interactions
            foreach (var oi in round.OperatorInteractions)
               Log(ExecutionLog.Info("Operator", $"{oi.RequesterName} → \"{Truncate(oi.Task, 100)}\" ({oi.ToolCallCount} tool call(s))"));

            // Log participant responses
            foreach (var (member, response) in round.Responses)
               Log(ExecutionLog.Trace("Participant", $"{member} responded ({response.Length} chars)"));

            OnRoundCompleted?.Invoke(round);
         },
         ct);

      Log(ExecutionLog.Info("Council", $"Debate completed — {result.Rounds.Count} rounds, duration: {result.TotalDuration.TotalSeconds:F1}s"));

      if (result.TokenStats is not null)
         Log(ExecutionLog.Info("Compression", $"Token stats — original: {result.TokenStats.TotalOriginalTokens:N0}, compressed: {result.TokenStats.TotalCompressedTokens:N0}, saved: {result.TokenStats.SavedPercent:F1}%"));

      if (!string.IsNullOrWhiteSpace(_outputPath))
      {
         await result.SaveToFileAsync(_outputPath);
         Log(ExecutionLog.Info("Output", $"Result saved to: {_outputPath}"));
      }

      // Return result with execution logs attached
      return result with { ExecutionLogs = _executionLogs.AsReadOnly() };
   }

   /// <summary>
   ///    Compresses text using the configured compressor, with optional caching.
   ///    Returns the original text unchanged if no compressor is configured.
   /// </summary>
   /// <param name="text">Text to compress.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Compressed context (or pass-through if no compressor).</returns>
   public async Task<CompressedContext> CompressTextAsync(string text, CancellationToken ct = default)
   {
      if (Compressor is null)
         return CompressedContextFactory.PassThrough(
            text, TokenCounter.Default.EstimateTokens(text), "None", TimeSpan.Zero);

      // Check cache
      if (CompressionCache?.TryGet(text, Compressor.StrategyName, out var cached) == true)
      {
         Log(ExecutionLog.Trace("Compression", $"Cache hit for {Compressor.StrategyName} ({text.Length} chars)"));
         return cached!;
      }

      Log(ExecutionLog.Trace("Compression", $"Compressing {text.Length} chars with {Compressor.StrategyName}..."));
      var result = await Compressor.CompressAsync(text, _compressionOptions, ct);
      Log(ExecutionLog.Info("Compression", $"Compressed: {result.OriginalTokens:N0} → {result.CompressedTokens:N0} tokens ({result.TokensSavedPercent:F1}% saved) via {result.StrategyUsed}"));

      // Store in cache
      CompressionCache?.Set(text, Compressor.StrategyName, result);

      return result;
   }

   /// <summary>
   ///    Returns a formatted summary of the council configuration.
   /// </summary>
   public string GetInfo()
   {
      var sb = new StringBuilder();
      sb.AppendLine("╔══════════════════════════════════════════╗");
      sb.AppendLine("║       LLM COUNCIL v3.1 CONFIGURATION     ║");
      sb.AppendLine("╚══════════════════════════════════════════╝");
      sb.AppendLine();
      sb.AppendLine($"  Strategy:    {Strategy.StrategyName}");
      sb.AppendLine($"  Max Rounds:  {_maxRounds}");
      sb.AppendLine($"  Temperature: {_temperature:F2}");
      if (ExecutionOptions.HasResponseLanguage)
         sb.AppendLine($"  Language:    {ExecutionOptions.ResponseLanguage}");
      if (ExecutionOptions.MaxDegreeOfParallelism > 0)
         sb.AppendLine($"  Parallelism: {ExecutionOptions.MaxDegreeOfParallelism}");
      if (ExecutionOptions.Logger is not null)
         sb.AppendLine($"  Logger:      {ExecutionOptions.Logger.GetType().Name}");
      sb.AppendLine();
      sb.AppendLine("  ── Members ──");
      foreach (var m in Members)
         sb.AppendLine($"    • {m.DisplayName} [{m.Role}]");

      if (Chairman is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Chairman ──");
         sb.AppendLine($"    ★ {Chairman.DisplayName}");
      }

      if (KnowledgeKeeper is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Knowledge Keeper ──");
         sb.AppendLine($"    📚 {KnowledgeKeeper.DisplayName} (collection: {KnowledgeKeeper.CollectionName})");
      }

      if (Operator is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Operator ──");
         sb.AppendLine($"    🛠️  {Operator.DisplayName} ({Operator.AvailableTools.Count} tool(s))");
      }

      if (Compressor is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Context Compression ──");
         sb.AppendLine($"    🗜️  Strategy: {Compressor.StrategyName}");
         if (_compressionOptions is not null)
         {
            sb.AppendLine($"    Target ratio: {_compressionOptions.TargetRatio:P0}");
            if (_compressionOptions.MaxOutputTokens.HasValue)
               sb.AppendLine($"    Max output tokens: {_compressionOptions.MaxOutputTokens}");
         }

         if (CompressionCache is not null)
            sb.AppendLine($"    Cache: enabled (max {CompressionCache.Count} entries)");
      }

      sb.AppendLine();
      sb.AppendLine("  ── Prompts ──");
      sb.AppendLine($"    System: {Truncate(_context.SystemPrompt, 80)}");
      sb.AppendLine($"    User:   {Truncate(_context.UserPrompt, 80)}");
      if (_context.KnowledgeFiles is { Count: > 0 })
         sb.AppendLine($"    Knowledge: {_context.KnowledgeFiles.Count} file(s)");
      if (!string.IsNullOrEmpty(_outputPath))
         sb.AppendLine($"    Output: {_outputPath}");

      return sb.ToString();
   }

   private void Log(ExecutionLog entry)
   {
      _executionLogs.Add(ExecutionLogSink.Emit(ExecutionOptions.Logger, entry));
      OnLog?.Invoke(entry);
   }

   private void ReportError(Exception ex, string context)
   {
      var entry = ExecutionLog.Error(context, ex.Message);
      _executionLogs.Add(ExecutionLogSink.Emit(ExecutionOptions.Logger, entry));
      OnError?.Invoke(ex, context);

      ExecutionOptions.Logger?.LogError(ex, "[{Source}] {Message}", context, ex.Message);
   }

   private static string Truncate(string text, int max)
   {
      return string.IsNullOrEmpty(text) ? "(empty)" : text.Length <= max ? text : text[..max] + "…";
   }
}