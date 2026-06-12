using Delibera.Core.Compression;

namespace Delibera.Core.Council;

/// <summary>
/// Executes a configured council debate session.
/// Created via <see cref="CouncilBuilder.Build"/>.
/// </summary>
public sealed class CouncilExecutor : ICouncilExecutor
{
   private readonly IReadOnlyList<CouncilMember> _members;
   private readonly CouncilMember? _chairman;
   private readonly KnowledgeKeeper? _knowledgeKeeper;
   private readonly IDebateStrategy _strategy;
   private readonly PromptContext _context;
   private readonly int _maxRounds;
   private readonly float _temperature;
   private readonly string? _outputPath;
   private readonly IContextCompressor? _compressor;
   private readonly CompressionOptions? _compressionOptions;
   private readonly CompressionCache? _compressionCache;
   private readonly List<ExecutionLog> _executionLogs = [];

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
       CompressionCache? compressionCache = null)
   {
      _members = members;
      _chairman = chairman;
      _knowledgeKeeper = knowledgeKeeper;
      _strategy = strategy;
      _context = context;
      _maxRounds = maxRounds;
      _temperature = temperature;
      _outputPath = outputPath;
      _compressor = compressor;
      _compressionOptions = compressionOptions;
      _compressionCache = compressionCache;
   }

   /// <summary>Council participants.</summary>
   public IReadOnlyList<CouncilMember> Members => _members;

   /// <summary>Chairman (may be <c>null</c>).</summary>
   public CouncilMember? Chairman => _chairman;

   /// <summary>Knowledge Keeper (may be <c>null</c>).</summary>
   public KnowledgeKeeper? KnowledgeKeeper => _knowledgeKeeper;

   /// <summary>Debate strategy.</summary>
   public IDebateStrategy Strategy => _strategy;

   /// <summary>Context compressor (may be <c>null</c> if compression is disabled).</summary>
   public IContextCompressor? Compressor => _compressor;

   /// <summary>Compression cache (may be <c>null</c>).</summary>
   public CompressionCache? CompressionCache => _compressionCache;

   /// <inheritdoc/>
   public IReadOnlyList<ExecutionLog> ExecutionLogs => _executionLogs.AsReadOnly();

   /// <summary>Invoked after each round completes.</summary>
   public event Action<DebateRound>? OnRoundCompleted;

   /// <summary>
   /// Runs the debate and returns the full result.
   /// </summary>
   public async Task<DebateResult> ExecuteAsync(CancellationToken ct = default)
   {
      _executionLogs.Clear();

      Log(ExecutionLog.Info("Council", $"Starting debate — strategy: {_strategy.StrategyName}, members: {_members.Count}, maxRounds: {_maxRounds}"));

      if (_chairman is not null)
         Log(ExecutionLog.Info("Chairman", $"Chairman assigned: {_chairman.DisplayName}"));

      if (_knowledgeKeeper is not null)
         Log(ExecutionLog.Info("KnowledgeKeeper", $"Knowledge Keeper ready: {_knowledgeKeeper.DisplayName} (collection: {_knowledgeKeeper.CollectionName})"));

      if (_compressor is not null)
         Log(ExecutionLog.Info("Compression", $"Compression enabled: {_compressor.StrategyName}"));

      foreach (var m in _members)
         Log(ExecutionLog.Trace("Council", $"Participant registered: {m.DisplayName} [{m.Role}]"));

      var result = await _strategy.ExecuteAsync(
          _members,
          _context,
          _chairman,
          _knowledgeKeeper,
          _maxRounds,
          _temperature,
          round =>
          {
             Log(ExecutionLog.Info("Council", $"Round {round.RoundNumber} completed: {round.RoundName} ({round.Duration.TotalSeconds:F1}s, {round.Responses.Count} responses)"));

             // Log knowledge interactions
             foreach (var ki in round.KnowledgeInteractions)
                Log(ExecutionLog.Info("KnowledgeKeeper", $"Query: \"{Truncate(ki.Query, 100)}\" → {ki.SourceChunks} chunks"));

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
   /// Compresses text using the configured compressor, with optional caching.
   /// Returns the original text unchanged if no compressor is configured.
   /// </summary>
   /// <param name="text">Text to compress.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Compressed context (or pass-through if no compressor).</returns>
   public async Task<CompressedContext> CompressTextAsync(string text, CancellationToken ct = default)
   {
      if (_compressor is null)
      {
         var tokens = TokenCounter.Default.EstimateTokens(text);
         return new CompressedContext
         {
            Text = text,
            OriginalLength = text.Length,
            CompressedLength = text.Length,
            OriginalTokens = tokens,
            CompressedTokens = tokens,
            StrategyUsed = "None"
         };
      }

      // Check cache
      if (_compressionCache is not null &&
          _compressionCache.TryGet(text, _compressor.StrategyName, out var cached) &&
          cached is not null)
      {
         Log(ExecutionLog.Trace("Compression", $"Cache hit for {_compressor.StrategyName} ({text.Length} chars)"));
         return cached;
      }

      Log(ExecutionLog.Trace("Compression", $"Compressing {text.Length} chars with {_compressor.StrategyName}..."));
      var result = await _compressor.CompressAsync(text, _compressionOptions, ct);
      Log(ExecutionLog.Info("Compression", $"Compressed: {result.OriginalTokens:N0} → {result.CompressedTokens:N0} tokens ({result.TokensSavedPercent:F1}% saved) via {result.StrategyUsed}"));

      // Store in cache
      _compressionCache?.Set(text, _compressor.StrategyName, result);

      return result;
   }

   /// <summary>
   /// Returns a formatted summary of the council configuration.
   /// </summary>
   public string GetInfo()
   {
      var sb = new StringBuilder();
      sb.AppendLine("╔══════════════════════════════════════════╗");
      sb.AppendLine("║       LLM COUNCIL v3.1 CONFIGURATION     ║");
      sb.AppendLine("╚══════════════════════════════════════════╝");
      sb.AppendLine();
      sb.AppendLine($"  Strategy:    {_strategy.StrategyName}");
      sb.AppendLine($"  Max Rounds:  {_maxRounds}");
      sb.AppendLine($"  Temperature: {_temperature:F2}");
      sb.AppendLine();
      sb.AppendLine("  ── Members ──");
      foreach (var m in _members)
         sb.AppendLine($"    • {m.DisplayName} [{m.Role}]");

      if (_chairman is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Chairman ──");
         sb.AppendLine($"    ★ {_chairman.DisplayName}");
      }

      if (_knowledgeKeeper is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Knowledge Keeper ──");
         sb.AppendLine($"    📚 {_knowledgeKeeper.DisplayName} (collection: {_knowledgeKeeper.CollectionName})");
      }

      if (_compressor is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Context Compression ──");
         sb.AppendLine($"    🗜️  Strategy: {_compressor.StrategyName}");
         if (_compressionOptions is not null)
         {
            sb.AppendLine($"    Target ratio: {_compressionOptions.TargetRatio:P0}");
            if (_compressionOptions.MaxOutputTokens.HasValue)
               sb.AppendLine($"    Max output tokens: {_compressionOptions.MaxOutputTokens}");
         }
         if (_compressionCache is not null)
            sb.AppendLine($"    Cache: enabled (max {_compressionCache.Count} entries)");
      }

      sb.AppendLine();
      sb.AppendLine("  ── Prompts ──");
      sb.AppendLine($"    System: {Truncate(_context.SystemPrompt, 80)}");
      sb.AppendLine($"    User:   {Truncate(_context.UserPrompt, 80)}");
      if (_context.KnowledgeFiles.Count > 0)
         sb.AppendLine($"    Knowledge: {_context.KnowledgeFiles.Count} file(s)");
      if (!string.IsNullOrEmpty(_outputPath))
         sb.AppendLine($"    Output: {_outputPath}");

      return sb.ToString();
   }

   private void Log(ExecutionLog entry) => _executionLogs.Add(entry);

   private static string Truncate(string text, int max) =>
       string.IsNullOrEmpty(text) ? "(empty)" : text.Length <= max ? text : text[..max] + "…";
}
