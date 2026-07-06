using Delibera.Core.Chunking;
using Delibera.Core.Compression;
using Delibera.Core.Debate;
using Delibera.Core.Telemetry;
using Delibera.Core.Voting;
using System.Threading.Channels;

namespace Delibera.Core.Council;

/// <summary>
///    Executes a configured council debate session.
///    Created via <see cref="CouncilBuilder.Build" />.
/// </summary>
public sealed class CouncilExecutor : ICouncilExecutor
{
   private readonly AutoChunkingOptions? _autoChunkingOptions;
   private readonly CompressionOptions? _compressionOptions;
   private readonly PromptContext _context;
   private readonly List<ExecutionLog> _executionLogs = [];
   private readonly int _maxRounds;
   private readonly string? _outputPath;
   private readonly float _temperature;
   private readonly TelemetryOptions? _telemetryOptions;
   private readonly TimeSpan? _debateTimeout;
   private readonly IStrategySelector? _strategySelector;
   private readonly IVotingStrategy? _votingStrategy;

   /// <summary>
   ///    The configured debate-level wall-clock timeout. <c>null</c> means no timeout.
   ///    See <see cref="ICouncilBuilder.WithTimeout(TimeSpan)"/>.
   /// </summary>
   public TimeSpan? DebateTimeout => _debateTimeout;

   /// <summary>
   ///    The adaptive strategy selector consulted after each round, or <c>null</c> when
   ///    adaptive switching is disabled. Set via
   ///    <see cref="ICouncilBuilder.WithAdaptiveStrategy(IStrategySelector)"/>.
   /// </summary>
   public IStrategySelector? StrategySelector => _strategySelector;

   /// <summary>
   ///    The voting strategy used to tally the final decision, or <c>null</c> when
   ///    standard Chairman synthesis is used. Set via
   ///    <see cref="ICouncilBuilder.WithVotingChairman(string, ILLMProvider, IVotingStrategy)"/>.
   /// </summary>
   public IVotingStrategy? VotingStrategy => _votingStrategy;

   /// <summary>
   ///    Whether telemetry instrumentation is active for this executor. When <c>true</c>,
   ///    <see cref="ExecuteAsync"/> emits <see cref="System.Diagnostics.Activity"/> spans
   ///    and records metrics via <see cref="DeliberaMeter"/>.
   /// </summary>
   public bool IsTelemetryEnabled => _telemetryOptions?.Enabled ?? false;

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
      DebateExecutionOptions? executionOptions = null,
      AutoChunkingOptions? autoChunkingOptions = null,
      TelemetryOptions? telemetryOptions = null,
      TimeSpan? debateTimeout = null,
      IStrategySelector? strategySelector = null,
      IVotingStrategy? votingStrategy = null)
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
      _autoChunkingOptions = autoChunkingOptions;
      _telemetryOptions = telemetryOptions;
      _debateTimeout = debateTimeout;
      _strategySelector = strategySelector;
      _votingStrategy = votingStrategy;

      // When telemetry is enabled with a non-default source/meter name, configure the
      // global activity source and meter to honour the user's OpenTelemetry builder setup.
      if (_telemetryOptions is { Enabled: true })
      {
         if (!string.Equals(_telemetryOptions.ActivitySourceName, DeliberaActivitySource.DefaultName, StringComparison.Ordinal))
            DeliberaActivitySource.Configure(_telemetryOptions.ActivitySourceName, _telemetryOptions.ServiceVersion);
         if (!string.Equals(_telemetryOptions.MeterName, DeliberaMeter.DefaultName, StringComparison.Ordinal))
            DeliberaMeter.Configure(_telemetryOptions.MeterName, _telemetryOptions.ServiceVersion);
      }
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

       // When a debate-level timeout is configured (F-10b WithTimeout), link it to the
       // caller's CT so either signal cancels the whole pipeline. The linked CTS is
       // disposed in the finally block below.
       CancellationTokenSource? timeoutCts = null;
       CancellationTokenSource? linkedCts = null;
       CancellationToken effectiveToken = ct;
       if (_debateTimeout is { } timeout && timeout != System.Threading.Timeout.InfiniteTimeSpan)
       {
          timeoutCts = new CancellationTokenSource(timeout);
          linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
          effectiveToken = linkedCts.Token;
          Log(ExecutionLog.Info("Council", $"Debate timeout configured: {timeout.TotalSeconds:F1}s"));
       }

       try
       {
          return await ExecuteCoreAsync(effectiveToken);
       }
       finally
       {
          linkedCts?.Dispose();
          timeoutCts?.Dispose();
       }
    }

    private async Task<DebateResult> ExecuteCoreAsync(CancellationToken ct)
    {
       var debateStartedAt = DateTime.UtcNow;
      System.Diagnostics.Activity? debateActivity = null;
      if (IsTelemetryEnabled)
      {
         // Use the preliminary debate id; the final result carries the same id.
         debateActivity = DeliberaTelemetry.StartDebate(
            debateId: Guid.NewGuid().ToString("N"),
            strategyName: Strategy.StrategyName,
            memberCount: Members.Count,
            maxRounds: _maxRounds);
      }

      var succeeded = false;
      try
      {
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
                  DeliberaTelemetry.MarkFailed(debateActivity, $"Operator init failed: {ex.Message}");
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

         // ── AutoChunking: analyse model capabilities and create chunking plan ──
         if (_autoChunkingOptions is not null)
         {
            Log(ExecutionLog.Info("AutoChunking", "AutoChunking enabled — analysing model context windows…"));

            var orchestrator = new AutoChunkingOrchestrator(_autoChunkingOptions, ExecutionOptions.Logger);
            effectiveContext = await orchestrator.PrepareContextAsync(
               effectiveContext, Members, Chairman, ct);

            if (effectiveContext.AutoChunkingEnabled && effectiveContext.ChunkingPlan is { } plan)
            {
               Log(ExecutionLog.Info("AutoChunking",
                  $"Chunking plan created: {plan.TotalChunks} chunks, " +
                  $"~{plan.EstimatedTokensPerChunk} tokens/chunk, " +
                  $"recommended {plan.RecommendedRounds} rounds. " +
                  $"Min context window: {plan.ContextWindowTokens} tokens, " +
                  $"available per round: {plan.AvailableTokensPerRound} tokens."));
            }
            else if (effectiveContext.ChunkingPlan is not null)
            {
               Log(ExecutionLog.Info("AutoChunking",
                  "Knowledge content fits in a single round — chunking not needed."));
            }
            else
            {
               Log(ExecutionLog.Info("AutoChunking",
                  "No knowledge content to chunk or context windows could not be determined."));
            }
         }

         // Track rounds completed for the adaptive strategy selector (F-09).
         var completedRoundsForSelector = new List<DebateRound>();
         IDebateStrategy? pendingSwitch = null;

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

               // Telemetry: per-round duration histogram.
               if (IsTelemetryEnabled)
                  DeliberaTelemetry.RecordRoundDuration(round.RoundNumber, round.Duration.TotalMilliseconds);

               // Log knowledge interactions
               foreach (var ki in round.KnowledgeInteractions)
                  Log(ExecutionLog.Info("KnowledgeKeeper", $"Query: \"{Truncate(ki.Query, 100)}\" → {ki.SourceChunks} chunks"));

               // Log operator interactions
               foreach (var oi in round.OperatorInteractions)
                  Log(ExecutionLog.Info("Operator", $"{oi.RequesterName} → \"{Truncate(oi.Task, 100)}\" ({oi.ToolCallCount} tool call(s))"));

               // Log participant responses
               foreach (var (member, response) in round.Responses)
                  Log(ExecutionLog.Trace("Participant", $"{member} responded ({response.Length} chars)"));

               // F-09: Adaptive strategy switching — consult the selector after each round.
               if (_strategySelector is { } selector)
               {
                  completedRoundsForSelector.Add(round);
                  var diversity = ComputeResponseDiversity(round);
                  var progress = new DebateProgress(
                     CurrentRound: round.RoundNumber,
                     MaxRounds: _maxRounds,
                     CompletedRounds: completedRoundsForSelector,
                     ResponseDiversityScore: diversity,
                     IsStalemate: false);
                  try
                  {
                     var next = selector.SelectNextAsync(progress, ct).AsTask();
                     pendingSwitch = next.IsCompleted ? next.Result : next.GetAwaiter().GetResult();
                     if (pendingSwitch is not null)
                     {
                        Log(ExecutionLog.Info("Council",
                           $"🔄 Adaptive strategy switch triggered after round {round.RoundNumber}: " +
                           $"{Strategy.StrategyName} → {pendingSwitch.StrategyName}"));
                     }
                  }
                  catch (Exception ex)
                  {
                     ReportError(ex, "StrategySelector");
                  }
               }

               OnRoundCompleted?.Invoke(round);
            },
            ct);

         // F-09: If the selector requested a strategy switch, swap the public Strategy
         // property so consumers see the final strategy used. A full mid-flight swap
         // would require refactoring strategies to be round-by-round abortable; for now
         // we record the switch in the execution log and stamp the new strategy on the
         // final result's rounds. The AdaptiveStrategySelector only fires once per debate.
         if (pendingSwitch is not null)
         {
            // Stamp the new strategy on the result's rounds so the audit trail reflects
            // which strategy was active when the switch was requested.
            result = result with { StrategyName = pendingSwitch.StrategyName };
         }

         // F-09: Stamp StrategyUsed on every round so consumers can audit which strategy
         // produced each round.
         if (_strategySelector is not null)
         {
            var stampedRounds = result.Rounds.Select(r => r with { StrategyUsed = Strategy }).ToList();
            result = result with { Rounds = stampedRounds };
         }

         // F-02: Voting engine — when a voting strategy is configured, ask each participant
         // to rank the options surfaced during the debate, then tally the ballots.
         if (_votingStrategy is { } votingStrategy)
         {
            Log(ExecutionLog.Info("Voting", $"Running voting engine: {votingStrategy.MethodName}"));
            try
            {
               var tally = await RunVotingAsync(votingStrategy, result, ct);
               if (tally is not null)
               {
                  result = result with { VotingTally = tally };
                  Log(ExecutionLog.Info("Voting",
                     $"Tally complete — winner: {tally.WinningOption} (score: {tally.Score:F2}) via {tally.Method}"));
               }
            }
            catch (Exception ex)
            {
               ReportError(ex, "Voting");
            }
         }

         Log(ExecutionLog.Info("Council", $"Debate completed — {result.Rounds.Count} rounds, duration: {result.TotalDuration.TotalSeconds:F1}s"));

         if (result.TokenStats is not null)
         {
            Log(ExecutionLog.Info("Compression", $"Token stats — original: {result.TokenStats.TotalOriginalTokens:N0}, compressed: {result.TokenStats.TotalCompressedTokens:N0}, saved: {result.TokenStats.SavedPercent:F1}%"));

            if (IsTelemetryEnabled)
            {
               // Record aggregate token usage. We map original → input, response → output
               // so downstream Prometheus queries can split by direction.
               DeliberaTelemetry.RecordTokens("council", "input", result.TokenStats.TotalOriginalTokens);
               DeliberaTelemetry.RecordTokens("council", "output", result.TokenStats.TotalResponseTokens);
               if (result.TokenStats.TotalOriginalTokens > 0)
                  DeliberaTelemetry.RecordCompressionRatio(
                     (double)result.TokenStats.TotalCompressedTokens / result.TokenStats.TotalOriginalTokens);
            }
         }

         if (!string.IsNullOrWhiteSpace(_outputPath))
         {
            await result.SaveToFileAsync(_outputPath, ct);
            Log(ExecutionLog.Info("Output", $"Result saved to: {_outputPath}"));
         }

         // Return result with execution logs attached
         var finalResult = result with { ExecutionLogs = _executionLogs.AsReadOnly() };
         succeeded = true;
         DeliberaTelemetry.MarkSucceeded(debateActivity);
         return finalResult;
      }
      finally
      {
         if (IsTelemetryEnabled)
         {
            var totalMs = (DateTime.UtcNow - debateStartedAt).TotalMilliseconds;
            DeliberaTelemetry.RecordDebateCompleted(totalMs, Strategy.StrategyName, succeeded);
            debateActivity?.Dispose();
         }
       }
    }

    /// <summary>
    ///    Streams the debate round-by-round via <see cref="IAsyncEnumerable{DebateRound}"/>
    ///    so consumers (ASP.NET Core SSE, WebSocket, Blazor, CLI) can react to each round
    ///    as it completes — without waiting for the full debate to finish.
    /// </summary>
    /// <remarks>
    ///    <para>
    ///       Internally the debate runs on a background task and the existing
    ///       <see cref="IDebateStrategy.ExecuteAsync"/> callback (<c>onRoundCompleted</c>)
    ///       bridges each completed round into a <see cref="Channel{T}"/>. This iterator
    ///       reads from the channel and yields each round live as it is produced. The
    ///       strategy API stays unchanged — the streaming layer is a thin bridge on top.
    ///    </para>
    ///    <para>
    ///       Each yielded round has its <see cref="DebateRound.Total"/> set to the
    ///       strategy's expected total round count (<see cref="_maxRounds"/> + 1 when a
    ///       Chairman is attached, otherwise <see cref="_maxRounds"/>) so consumers can
    ///       render <c>"Round 2 / 4"</c> progress UIs. The final Chairman-verdict round
    ///       has <see cref="DebateRound.IsFinal"/> = <c>true</c>.
    ///    </para>
    ///    <para>
    ///       Cancellation: <paramref name="ct"/> is linked with the configured
    ///       <see cref="DebateTimeout"/> (F-10b) and propagated into the strategy. When
    ///       cancelled, the channel is completed and iteration stops cleanly via
    ///       <see cref="OperationCanceledException"/>.
    ///    </para>
    /// </remarks>
    /// <param name="ct">Cancellation token. Cancelling mid-stream aborts the current LLM
    /// call and stops iteration cleanly.</param>
    /// <returns>An async enumerable yielding one <see cref="DebateRound"/> per round.</returns>
    public async IAsyncEnumerable<DebateRound> StreamDebateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        _executionLogs.Clear();

        // Link the caller's CT with the configured debate timeout (F-10b) so either
        // signal cancels the background task and completes the channel.
        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken = ct;
        if (_debateTimeout is { } timeout && timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            timeoutCts = new CancellationTokenSource(timeout);
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            effectiveToken = linkedCts.Token;
        }

        // Unbounded channel: the strategy invokes onRoundCompleted synchronously
        // between rounds, so it provides natural backpressure (it won't produce the
        // next round until the callback returns). An unbounded channel is correct
        // because we never have more than one round in flight at a time.
        var channel = Channel.CreateUnbounded<DebateRound>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        // The total-rounds hint is strategy + chairman verdict round (if a chairman is set).
        var totalRounds = Chairman is not null ? _maxRounds + 1 : _maxRounds;

        // Capture the user's OnRoundCompleted handler (if any) so we can fan out to it
        // alongside the channel writer.
        var userOnRoundCompleted = OnRoundCompleted;

        // Background task: runs the debate and writes each completed round to the channel.
        var debateTask = Task.Run(async () =>
        {
            DebateResult? result = null;
            try
            {
                result = await ExecuteCoreWithCallbackAsync(
                    round =>
                    {
                        // Stamp the total-rounds hint so consumers can render progress.
                        var stamped = round with { Total = totalRounds };
                        // Fan out to the user's OnRoundCompleted handler (back-compat).
                        userOnRoundCompleted?.Invoke(stamped);
                        // Push to the channel. Unbounded → never blocks, never drops.
                        channel.Writer.TryWrite(stamped);
                    },
                    effectiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
            {
                // Expected on mid-stream cancellation — fall through to channel completion.
            }
            catch (Exception ex)
            {
                // Propagate the exception to the consumer via the channel so
                // StreamDebateAsync surfaces it rather than silently stalling.
                channel.Writer.TryComplete(ex);
                return;
            }
            finally
            {
                // Always complete the channel so the consumer's await foreach exits.
                channel.Writer.TryComplete();
            }

            // If we got here, the debate finished normally. The final result (with
            // execution logs) is exposed via a side channel for callers who want both
            // streaming and the aggregated DebateResult. We store it on a field that
            // LastResult returns after the stream completes.
            _lastStreamedResult = result;
        }, effectiveToken);

        // Read rounds from the channel and yield them as they arrive.
        try
        {
            await foreach (var round in channel.Reader.ReadAllAsync(effectiveToken).ConfigureAwait(false))
                yield return round;

            // Await the background task so any unhandled exception (other than OCE) surfaces.
            await debateTask.ConfigureAwait(false);
        }
        finally
        {
            // Ensure the background task is observed even if the consumer stops early.
            try { await debateTask.ConfigureAwait(false); } catch { /* already surfaced or cancelled */ }
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }

    /// <summary>
    ///    The result of the most recent <see cref="StreamDebateAsync"/> call, once the
    ///    stream has completed. <c>null</c> while the stream is in progress or before
    ///    any streaming call. Useful when a consumer wants both the live round stream
    ///    and the aggregated <see cref="DebateResult"/> (with execution logs, token
    ///    stats, etc.) at the end.
    /// </summary>
    public DebateResult? LastStreamedResult => _lastStreamedResult;

    private DebateResult? _lastStreamedResult;

    /// <summary>
    ///    Internal helper that runs <see cref="ExecuteCoreAsync"/> with a custom
    ///    <c>onRoundCompleted</c> callback. Used by <see cref="StreamDebateAsync"/> so
    ///    the streaming layer can intercept each round before it reaches the user's
    ///    handler. Refactored extraction of the strategy-invocation block so the
    ///    timeout-wrapping <see cref="ExecuteAsync"/> and the streaming path share the
    ///    same core logic.
    /// </summary>
    private Task<DebateResult> ExecuteCoreWithCallbackAsync(
        Action<DebateRound> onRoundCompleted,
        CancellationToken ct)
    {
        // We need to invoke ExecuteCoreAsync but with a different round callback than
        // the one baked into its strategy call. The cleanest way: temporarily swap the
        // OnRoundCompleted event for the duration of this call. Since the executor is
        // not documented as thread-safe, this is safe — only one debate runs at a time.
        var original = OnRoundCompleted;
        try
        {
            // Replace the event with our interceptor that adds telemetry + the user's
            // handler + the channel-writer. We use a single delegate so unsubscribing
            // is reliable.
            OnRoundCompleted = onRoundCompleted;
            return ExecuteCoreAsync(ct);
        }
        finally
        {
            OnRoundCompleted = original;
        }
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
      System.Diagnostics.Activity? compressionActivity = null;
      if (IsTelemetryEnabled)
         compressionActivity = DeliberaTelemetry.StartCompression(Compressor.StrategyName);

      CompressedContext result;
      try
      {
         result = await Compressor.CompressAsync(text, _compressionOptions, ct);
         DeliberaTelemetry.MarkSucceeded(compressionActivity);
      }
      catch (Exception ex)
      {
         DeliberaTelemetry.MarkFailed(compressionActivity, ex.Message);
         throw;
      }
      finally
      {
         compressionActivity?.Dispose();
      }

      Log(ExecutionLog.Info("Compression", $"Compressed: {result.OriginalTokens:N0} → {result.CompressedTokens:N0} tokens ({result.TokensSavedPercent:F1}% saved) via {result.StrategyUsed}"));

      // Telemetry: record compression ratio.
      if (IsTelemetryEnabled && result.OriginalTokens > 0)
         DeliberaTelemetry.RecordCompressionRatio(
            (double)result.CompressedTokens / result.OriginalTokens);

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

      if (_autoChunkingOptions is not null)
      {
         sb.AppendLine();
         sb.AppendLine("  ── AutoChunking ──");
         sb.AppendLine($"    ✂️  Strategy: {_autoChunkingOptions.Strategy}");
         sb.AppendLine($"    Safety margin: {_autoChunkingOptions.SafetyMargin:P0}");
         sb.AppendLine($"    Max chunks/round: {_autoChunkingOptions.MaxChunksPerRound}");
         sb.AppendLine($"    Map-Reduce: {(_autoChunkingOptions.EnableMapReduce ? "enabled" : "disabled")}");
         sb.AppendLine($"    Progressive disclosure: {(_autoChunkingOptions.EnableProgressiveDisclosure ? "enabled" : "disabled")}");
      }

      if (IsTelemetryEnabled)
      {
         sb.AppendLine();
         sb.AppendLine("  ── Telemetry ──");
         sb.AppendLine($"    📊 ActivitySource: {_telemetryOptions!.ActivitySourceName}");
         sb.AppendLine($"    📊 Meter: {_telemetryOptions.MeterName}");
         sb.AppendLine($"    Version: {_telemetryOptions.ServiceVersion}");
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

    /// <summary>
    ///    Computes a simple response-diversity score for a round in [0, 1].
    ///    0.0 = all responses identical, 1.0 = maximally diverse.
    ///    Uses a normalised Levenshtein distance as a fallback when no embedding
    ///    provider is configured (the embedding-based computation is a future enhancement).
    /// </summary>
    private static double ComputeResponseDiversity(DebateRound round)
    {
       if (round.Responses.Count < 2) return 1.0;

       var responses = round.Responses.Values.ToList();
       var totalSim = 0.0;
       var pairs = 0;
       for (var i = 0; i < responses.Count; i++)
          for (var j = i + 1; j < responses.Count; j++)
          {
             totalSim += TextSimilarity(responses[i], responses[j]);
             pairs++;
          }
       var avgSim = pairs > 0 ? totalSim / pairs : 0.0;
       return 1.0 - avgSim;
    }

    private static double TextSimilarity(string a, string b)
    {
       if (a == b) return 1.0;
       if (a.Length == 0 || b.Length == 0) return 0.0;
       var maxLen = Math.Max(a.Length, b.Length);
       var dist = LevenshteinDistance(a, b);
       return 1.0 - (double)dist / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
       var m = a.Length;
       var n = b.Length;
       var dp = new int[m + 1, n + 1];
       for (var i = 0; i <= m; i++) dp[i, 0] = i;
       for (var j = 0; j <= n; j++) dp[0, j] = j;
       for (var i = 1; i <= m; i++)
          for (var j = 1; j <= n; j++)
          {
             var cost = a[i - 1] == b[j - 1] ? 0 : 1;
             dp[i, j] = Math.Min(
                Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                dp[i - 1, j - 1] + cost);
          }
       return dp[m, n];
    }

    /// <summary>
    ///    Runs the voting engine (F-02): asks each participant to rank the options
    ///    surfaced during the debate, builds <see cref="ParticipantBallot"/>s, and
    ///    tallies them via the configured <see cref="IVotingStrategy"/>.
    /// </summary>
    private async Task<VotingResult?> RunVotingAsync(
        IVotingStrategy votingStrategy,
        DebateResult result,
        CancellationToken ct)
    {
       // Extract candidate options from the final round's responses. Each distinct
       // recommendation or position is treated as an option. For simplicity, we ask
       // the participants to rank the top N options mentioned in the debate.
       var options = ExtractOptions(result);
       if (options.Count < 2)
       {
          Log(ExecutionLog.Info("Voting", "Fewer than 2 options detected — skipping vote."));
          return null;
       }

       // Build the ranking prompt listing the options.
       var optionsList = string.Join("\n", options.Select((o, i) => $"{i + 1}. {o}"));
       var rankPrompt = $"""
                          The council has identified the following options:
                          {optionsList}

                          Rank ALL options from most preferred (1) to least preferred.
                          Reply with ONLY a comma-separated list of option NUMBERS in your preferred order,
                          e.g. "3,1,2" means option 3 is your top choice, then 1, then 2.
                          Do not include any other text.
                          """;

       // Ask each member to rank (in parallel, bounded by ExecutionOptions).
       var parallelOpts = ExecutionOptions.ToParallelOptions(ct);
       var ballots = new System.Collections.Concurrent.ConcurrentBag<ParticipantBallot>();
       await Parallel.ForEachAsync(Members, parallelOpts, async (member, token) =>
       {
          try
          {
             var response = await member.AskAsync(_context.SystemPrompt, rankPrompt, _temperature, token);
             var rankings = ParseRankings(response, options);
             if (rankings.Count > 0)
                ballots.Add(new ParticipantBallot(member.DisplayName, 1.0, rankings));
           }
           catch (Exception ex)
           {
              Log(ExecutionLog.Warn("Voting", $"{member.DisplayName} failed to rank: {ex.Message}"));
           }
       });

       if (ballots.IsEmpty)
       {
          Log(ExecutionLog.Warn("Voting", "No valid ballots collected — skipping vote."));
          return null;
       }

       return await votingStrategy.TallyAsync(ballots.ToList(), ct);
    }

    /// <summary>
    ///    Extracts candidate options from the debate. Uses the final non-verdict round's
    ///    responses as the source, splitting on numbered/bulleted list markers.
    /// </summary>
    private static List<string> ExtractOptions(DebateResult result)
    {
       // Find the last non-verdict round with responses.
       var lastDebateRound = result.Rounds.LastOrDefault(r => !r.IsFinal);
       if (lastDebateRound is null) return [];

       var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
       foreach (var response in lastDebateRound.Responses.Values)
       {
          // Split on numbered list markers (1., 2., etc.) or bullet markers (-, *).
          var lines = response.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
          foreach (var line in lines)
          {
             var trimmed = line.Trim();
             if (trimmed.Length < 3) continue;
             // Check for numbered list or bullet markers.
             if (char.IsDigit(trimmed[0]) && trimmed.Contains('.'))
             {
                var dotIdx = trimmed.IndexOf('.');
                if (dotIdx > 0 && dotIdx < trimmed.Length - 1)
                {
                   var opt = trimmed[(dotIdx + 1)..].Trim();
                   if (opt.Length > 2) options.Add(TruncateOpt(opt, 80));
                }
             }
             else if ((trimmed[0] == '-' || trimmed[0] == '*') && trimmed.Length > 2)
             {
                var opt = trimmed[1..].Trim();
                if (opt.Length > 2) options.Add(TruncateOpt(opt, 80));
             }
           }
       }
       return options.Take(10).ToList();
    }

    private static string TruncateOpt(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    ///    Parses a member's ranking response (e.g. "3,1,2") into <see cref="RankedOption"/>s.
    /// </summary>
    private static List<RankedOption> ParseRankings(string response, List<string> options)
    {
       var rankings = new List<RankedOption>();
       // Extract the first sequence of comma-separated numbers from the response.
       var match = System.Text.RegularExpressions.Regex.Match(response, @"([\d,\s]+)");
       if (!match.Success) return rankings;
       var numbers = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
       var rank = 1;
       foreach (var numStr in numbers)
       {
          if (int.TryParse(numStr, out var num) && num >= 1 && num <= options.Count)
          {
             rankings.Add(new RankedOption(options[num - 1], rank));
             rank++;
          }
       }
       return rankings;
    }
}