using Delibera.Core.Attachments;
using Delibera.Core.Caching;
using Delibera.Core.Chunking;
using Delibera.Core.Compression;
using Delibera.Core.Debate;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Memory;
using Delibera.Core.Output;
using Delibera.Core.Persistence;
using Delibera.Core.Providers.Mcp;
using Delibera.Core.Telemetry;
using Delibera.Core.Voting;

namespace Delibera.Core.Council;

/// <summary>
///    Fluent builder for assembling and configuring a council debate session.
/// </summary>
public sealed class CouncilBuilder : ICouncilBuilder
{
   private const string DefaultStrategy = "Standard";
   private const int DefaultMaxRounds = 4;
   private const float DefaultTemperature = 0.7f;
   private const string DefaultSystemPrompt = "You are a helpful AI assistant participating in a council debate.";
   private readonly List<CouncilMember> _members = [];
   private IAgentMemory? _agentMemory;
   private AutoChunkingOptions? _autoChunkingOptions;
   private CouncilMember? _chairman;
   private CompressionCache? _compressionCache;
   private CompressionOptions? _compressionOptions;
   private IContextCompressor? _compressor;
   private IDebateStore? _debateStore;
   private TimeSpan? _debateTimeout;
   private IKnowledgeBase? _knowledgeBase;
   private KnowledgeKeeper? _knowledgeKeeper;
   private ILogger? _logger;
   private int _maxDegreeOfParallelism;
   private int? _maxParticipants;
   private int _maxRounds = 4;
   private Operator? _operator;
   private CouncilMember? _operatorModel;
   private bool _operatorReuseCompression;
   private IReadOnlyList<McpServerConfig>? _operatorServers;
   private string? _outputPath;
   private CouncilOptions? _persistedOptionsSnapshot;
   private string? _responseLanguage;
   private string? _resumeFromDebateId;
   private IDebateStrategy _strategy = new StandardDebate();
   private IStrategySelector? _strategySelector;
   private IStructuredOutputSerializer? _structuredOutputSerializer;
   private Type? _structuredOutputType;
   private string _systemPrompt = "You are a helpful AI assistant participating in a council debate.";
   private TelemetryOptions? _telemetryOptions;
   private float _temperature = 0.7f;
   private string _userPrompt = string.Empty;
    private IVotingStrategy? _votingStrategy;
    private CacheBehavior _cacheBehavior;
    private IDebateCache? _cache;
   private readonly List<FileAttachment> _attachments = [];
   private readonly FileContentReaderRegistry _fileReaders = new();

   /// <summary>
   ///    Creates an empty builder. Use <see cref="WithOptions(CouncilOptions)" /> or
   ///    <see cref="WithOptions(Action{CouncilOptions})" /> to apply pre-built configuration.
   /// </summary>
   public CouncilBuilder()
   {
   }

   /// <summary>
   ///    Creates a builder pre-configured from a <see cref="CouncilOptions" /> snapshot.
   ///    Equivalent to <c>new CouncilBuilder().WithOptions(options)</c>.
   /// </summary>
   /// <param name="options">Configuration to apply immediately.</param>
   public CouncilBuilder(CouncilOptions options)
   {
      ArgumentNullException.ThrowIfNull(options);
      ApplyOptions(options);
   }

   // ── Members ──

   /// <inheritdoc />
   public ICouncilBuilder AddMember(CouncilMember member)
   {
      ArgumentNullException.ThrowIfNull(member);
      _members.Add(member);
      return this;
   }

    /// <inheritdoc />
    public ICouncilBuilder AddMember(string modelName, ILLMProvider provider, string? role = null, string? persona = null)
    {
       var member = new CouncilMember(modelName, provider, role, persona);
       // Auto-detect capabilities from model name.
       if (ModelContextWindowRegistry.SupportsVision(modelName))
          member.Capabilities |= MemberCapabilities.Vision;
       _members.Add(member);
       return this;
    }

    // ── Members with capabilities (F-06 Multi-Modal) ──

    /// <summary>
    ///    Adds a participant with explicit <see cref="MemberCapabilities"/>. When
    ///    <see cref="MemberCapabilities.Vision"/> is set, the member receives image
    ///    attachments as <c>ImageContent</c> via Microsoft.Extensions.AI; text-only
    ///    members receive a textual placeholder for binary attachments.
    /// </summary>
    /// <param name="modelName">Model name (e.g. "llava:13b").</param>
    /// <param name="provider">LLM provider instance.</param>
    /// <param name="role">Role label.</param>
    /// <param name="capabilities">
    ///    Member capabilities. Pass <see cref="MemberCapabilities.Vision"/> |
    ///    <see cref="MemberCapabilities.Text"/> for a vision-capable member.
    ///    Vision is auto-detected from the model name when not set here.
    /// </param>
    /// <param name="persona">Optional persona prompt.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public ICouncilBuilder AddMember(string modelName, ILLMProvider provider, string role,
        MemberCapabilities capabilities, string? persona = null)
    {
       var member = new CouncilMember(modelName, provider, role, persona)
       {
          Capabilities = capabilities | (ModelContextWindowRegistry.SupportsVision(modelName) ? MemberCapabilities.Vision : 0)
       };
       _members.Add(member);
       return this;
    }

   // ── Chairman ──

   /// <inheritdoc />
   public ICouncilBuilder SetChairman(CouncilMember chairman)
   {
      ArgumentNullException.ThrowIfNull(chairman);
      _chairman = chairman;
      _chairman.Role = "Chairman";
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder SetChairman(string modelName, ILLMProvider provider, string? persona = null)
   {
      _chairman = new CouncilMember(modelName, provider, "Chairman", persona);
      return this;
   }

   // ── Knowledge Keeper ──

   /// <inheritdoc />
   public ICouncilBuilder WithKnowledgeKeeper(KnowledgeKeeper knowledgeKeeper)
   {
      _knowledgeKeeper = knowledgeKeeper ?? throw new ArgumentNullException(nameof(knowledgeKeeper));
      return this;
   }

   // ── Operator (MCP tool micro-agent) ──

   /// <inheritdoc />
   public ICouncilBuilder WithOperator(Operator @operator)
   {
      _operator = @operator ?? throw new ArgumentNullException(nameof(@operator));
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithOperator(
      string modelName,
      ILLMProvider provider,
      IEnumerable<McpServerConfig> servers,
      bool reuseCompression = true)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
      ArgumentNullException.ThrowIfNull(provider);
      ArgumentNullException.ThrowIfNull(servers);

      var serverList = servers.ToList();
      if (serverList.Count == 0)
         throw new ArgumentException("At least one MCP server configuration is required.", nameof(servers));

      // Operator construction is deferred to Build() so it can reuse the council's compressor.
      _operatorModel = new CouncilMember(modelName, provider, "Operator");
      _operatorServers = serverList;
      _operatorReuseCompression = reuseCompression;
      return this;
   }

   // ── Strategy ──

   /// <inheritdoc />
   public ICouncilBuilder WithStrategy(IDebateStrategy strategy)
   {
      _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
      return this;
   }

   // ── Knowledge base (legacy MD-based) ──

   /// <inheritdoc />
   public ICouncilBuilder WithKnowledge(IKnowledgeBase knowledgeBase)
   {
      _knowledgeBase = knowledgeBase;
      return this;
   }

   // ── Context Compression ──

   /// <inheritdoc />
   public ICouncilBuilder WithCompression(IContextCompressor compressor, CompressionOptions? options = null)
   {
      _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
      _compressionOptions = options;
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithCompression(
      CompressionStrategy strategy,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null,
      CompressionOptions? options = null)
   {
      _compressor = CompressionFactory.Create(strategy, llmProvider, modelName, embeddingProvider);
      _compressionOptions = options;
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithCompressionOptions(CompressionOptions options)
   {
      _compressionOptions = options ?? throw new ArgumentNullException(nameof(options));
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithCompressionCache(int maxEntries = 256)
   {
      _compressionCache = new CompressionCache(maxEntries);
      return this;
   }

   // ── Prompts / parameters ──

   /// <inheritdoc />
   public ICouncilBuilder WithSystemPrompt(string systemPrompt)
   {
      _systemPrompt = systemPrompt;
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithUserPrompt(string userPrompt)
   {
      _userPrompt = userPrompt;
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithMaxRounds(int maxRounds)
   {
      _maxRounds = Math.Clamp(maxRounds, 1, 10);
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithTemperature(float temperature)
   {
      _temperature = Math.Clamp(temperature, 0f, 2f);
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder SaveResultTo(string outputPath)
   {
      _outputPath = outputPath;
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithResponseLanguage(string? language)
   {
      _responseLanguage = string.IsNullOrWhiteSpace(language)
         ? null
         : language.Trim();
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithMaxDegreeOfParallelism(int maxDegreeOfParallelism)
   {
      _maxDegreeOfParallelism = Math.Max(0, maxDegreeOfParallelism);
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithLogger(ILogger? logger)
   {
      _logger = logger;
      return this;
   }

   // ── AutoChunking ──

   /// <inheritdoc />
   public ICouncilBuilder WithAutoChunking(AutoChunkingOptions? options = null)
   {
      _autoChunkingOptions = options ?? AutoChunkingOptions.Default;
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithModelContextWindow(string modelNamePattern, int contextWindowTokens)
   {
      ModelContextWindowRegistry.Register(modelNamePattern, contextWindowTokens);
      return this;
   }

   // ── Telemetry (OpenTelemetry-style observability) ──

   /// <summary>
   ///    Enables OpenTelemetry-style observability. When enabled, the
   ///    <see cref="CouncilExecutor" /> emits <see cref="System.Diagnostics.Activity" />
   ///    spans via <see cref="DeliberaActivitySource" /> and records metrics via
   ///    <see cref="DeliberaMeter" />. See <see cref="TelemetryOptions" /> for the
   ///    activity-source / meter naming convention.
   /// </summary>
   /// <param name="options">
   ///    Telemetry configuration. Pass <c>null</c> to use defaults
   ///    (<see cref="TelemetryOptions.Enabled" /> = <c>true</c>, default source/meter names).
   /// </param>
   /// <returns>This builder for fluent chaining.</returns>
   public ICouncilBuilder WithTelemetry(TelemetryOptions? options = null)
   {
      _telemetryOptions = options ?? new TelemetryOptions { Enabled = true };
      return this;
   }

   /// <summary>
   ///    Enables OpenTelemetry-style observability with a configuration delegate.
   /// </summary>
   /// <param name="configure">Delegate that populates a fresh <see cref="TelemetryOptions" />.</param>
   /// <returns>This builder for fluent chaining.</returns>
   public ICouncilBuilder WithTelemetry(Action<TelemetryOptions> configure)
   {
      ArgumentNullException.ThrowIfNull(configure);
      _telemetryOptions = new TelemetryOptions { Enabled = true };
      configure(_telemetryOptions);
      return this;
   }

   // ── Quick Wins (F-10) ──

   /// <summary>
   ///    Sets a hard wall-clock timeout for the whole debate. When the timeout
   ///    elapses, the internal <c>CancellationTokenSource</c> used by
   ///    <see cref="ICouncilExecutor.ExecuteAsync(CancellationToken)" /> is cancelled,
   ///    which propagates <see cref="OperationCanceledException" /> through every
   ///    downstream async operation (LLM calls, RAG queries, MCP tools, file saves).
   /// </summary>
   /// <param name="timeout">Maximum debate duration. <see cref="Timeout.InfiniteTimeSpan" /> disables.</param>
   /// <returns>This builder for fluent chaining.</returns>
   /// <remarks>
   ///    <para>
   ///       This is a convenience wrapper over <c>CancellationTokenSource</c>. Callers
   ///       who already own a token can pass it directly to
   ///       <see cref="ICouncilExecutor.ExecuteAsync(CancellationToken)" /> instead —
   ///       the two mechanisms compose via
   ///       <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)" />.
   ///    </para>
   /// </remarks>
   public ICouncilBuilder WithTimeout(TimeSpan timeout)
   {
      if (timeout == TimeSpan.Zero)
         throw new ArgumentOutOfRangeException(nameof(timeout), "Use Timeout.InfiniteTimeSpan to disable the timeout, not TimeSpan.Zero.");
      _debateTimeout = timeout;
      return this;
   }

   /// <summary>
   ///    Caps the maximum number of council participants. When set,
   ///    <see cref="Build" /> throws <see cref="InvalidOperationException" /> if more
   ///    members have been added than the limit. Useful in dynamic DI-driven setups
   ///    where the participant list is built at runtime and a misconfigured source
   ///    could enqueue dozens of models.
   /// </summary>
   /// <param name="maxParticipants">Maximum allowed participants (must be ≥ 1).</param>
   /// <returns>This builder for fluent chaining.</returns>
   public ICouncilBuilder WithParticipantLimit(int maxParticipants)
   {
      if (maxParticipants < 1)
         throw new ArgumentOutOfRangeException(nameof(maxParticipants), "Participant limit must be at least 1.");
      _maxParticipants = maxParticipants;
      return this;
   }

   // ── Adaptive strategy switching (F-09) ──

   /// <summary>
   ///    Enables adaptive strategy switching. After each round,
   ///    <see cref="CouncilExecutor" /> calls
   ///    <see cref="IStrategySelector.SelectNextAsync" /> with a
   ///    <see cref="DebateProgress" /> snapshot; if the selector returns a non-null
   ///    strategy, the executor swaps <see cref="ICouncilExecutor.Strategy" /> before
   ///    the next round. Use <see cref="AdaptiveStrategySelector" /> for the built-in
   ///    stalemate detector, or implement <see cref="IStrategySelector" /> for custom
   ///    heuristics.
   /// </summary>
   /// <param name="selector">The strategy selector to consult after each round.</param>
   /// <returns>This builder for fluent chaining.</returns>
   /// <remarks>
   ///    When <see cref="AdaptiveStrategySelector" /> is used, its <see cref="AdaptiveStrategySelector.Initial" />
   ///    is set as the council's starting strategy (overriding any prior
   ///    <see cref="WithStrategy" /> call).
   /// </remarks>
   public ICouncilBuilder WithAdaptiveStrategy(IStrategySelector selector)
   {
      ArgumentNullException.ThrowIfNull(selector);
      _strategySelector = selector;
      // If the selector is an AdaptiveStrategySelector, adopt its Initial strategy
      // as the council's starting strategy so the debate begins with the right one.
      if (selector is AdaptiveStrategySelector adaptive)
         _strategy = adaptive.Initial;
      return this;
   }

   // ── Voting engine (F-02) ──

   /// <summary>
   ///    Configures a voting Chairman that uses an <see cref="IVotingStrategy" /> to
   ///    reach a decision via structured voting among participants, as an alternative
   ///    to the single-LLM Chairman synthesis. The strategy is attached to the
   ///    Chairman member and detected by <see cref="CouncilExecutor" /> at runtime.
   /// </summary>
   /// <param name="modelName">Chairman model name.</param>
   /// <param name="provider">LLM provider for the Chairman (used to ask participants to rank options).</param>
   /// <param name="votingStrategy">Voting strategy (Majority, BordaCount, Weighted, ...).</param>
   /// <returns>This builder for fluent chaining.</returns>
   public ICouncilBuilder WithVotingChairman(string modelName, ILLMProvider provider, IVotingStrategy votingStrategy)
   {
      ArgumentNullException.ThrowIfNull(votingStrategy);
      _votingStrategy = votingStrategy;
      SetChairman(Chairman.CreateVoting(modelName, provider, votingStrategy));
      return this;
   }

   /// <summary>
   ///    Configures a voting strategy (F-02) that uses an <see cref="IVotingStrategy" />
   /// </summary>
   /// <param name="votingStrategy"></param>
   /// <returns></returns>
   public ICouncilBuilder WithVoting(IVotingStrategy votingStrategy)
   {
      _votingStrategy = votingStrategy;
      return this;
   }

   // ── Structured output (F-05) ──

   /// <summary>
   ///    Enables structured JSON output (F-05). The Chairman's synthesis prompt is
   ///    augmented with a JSON schema generated from <typeparamref name="TVerdict" />,
   ///    and <see cref="ICouncilExecutor.ExecuteTypedAsync{TVerdict}" /> deserialises
   ///    the response into a strongly-typed verdict. One automatic retry with a
   ///    correction prompt is performed on deserialisation failure.
   /// </summary>
   /// <typeparam name="TVerdict">The target verdict type (typically a C# record).</typeparam>
   /// <param name="serializer">
   ///    Optional custom serializer. <c>null</c> uses <see cref="JsonSchemaOutputSerializer" />
   ///    with default options.
   /// </param>
   /// <returns>This builder for fluent chaining.</returns>
   public ICouncilBuilder WithStructuredOutput<TVerdict>(IStructuredOutputSerializer? serializer = null) where TVerdict : class
   {
      _structuredOutputSerializer = serializer ?? new JsonSchemaOutputSerializer();
      _structuredOutputType = typeof(TVerdict);
      return this;
   }

   // ── Debate persistence (F-03) ──

   /// <summary>
   ///    Attaches an <see cref="IDebateStore" /> so a checkpoint is saved after every
   ///    round. The debate can be resumed from the last completed round after a
   ///    crash or intentional pause via <see cref="ResumeFrom" />.
   /// </summary>
   /// <param name="store">The store to persist checkpoints to.</param>
   /// <returns>This builder for fluent chaining.</returns>
   public ICouncilBuilder WithPersistence(IDebateStore store)
   {
      ArgumentNullException.ThrowIfNull(store);
      _debateStore = store;
      return this;
   }

   /// <summary>
   ///    Resumes a debate from the given <paramref name="debateId" />. The corresponding
   ///    checkpoint must exist in the configured <see cref="IDebateStore" /> (set via
   ///    <see cref="WithPersistence" />). The executor skips already-completed rounds
   ///    and continues from the next round using the checkpoint's preserved options.
   /// </summary>
   /// <param name="debateId">The debate identifier to resume.</param>
   /// <returns>This builder for fluent chaining.</returns>
    public ICouncilBuilder ResumeFrom(string debateId)
    {
       ArgumentException.ThrowIfNullOrWhiteSpace(debateId);
       _resumeFromDebateId = debateId;
       return this;
    }

    /// <inheritdoc />
    public ICouncilBuilder WithCacheBehavior(CacheBehavior behavior)
    {
       _cacheBehavior = behavior;
       return this;
    }

    /// <summary>
    ///    Sets the caching behavior and cache backend for this debate.
    /// </summary>
    public ICouncilBuilder WithCache(CacheBehavior behavior, IDebateCache cache)
    {
       _cacheBehavior = behavior;
       _cache = cache;
       return this;
    }

    // ── Agent memory (F-04) ──

   /// <summary>
   ///    Attaches an <see cref="IAgentMemory" /> so council members can recall
   ///    context from previous sessions and persist their conclusions after each
   ///    debate. The default is <see cref="InMemoryAgentMemory" /> (no persistence)
   ///    when not configured.
   /// </summary>
   /// <param name="memory">Memory backend. <c>null</c> disables memory.</param>
   /// <returns>This builder for fluent chaining.</returns>
    public ICouncilBuilder WithAgentMemory(IAgentMemory? memory = null)
    {
       _agentMemory = memory ?? new InMemoryAgentMemory();
       return this;
    }

    // ── Multi-Modal attachments (F-06) ──

    /// <summary>
    ///    Attaches a file to the debate. The file is read lazily by the
    ///    <see cref="FileContentReaderRegistry"/> when the debate starts.
    ///    Vision-capable members receive image attachments as
    ///    <c>ImageContent</c>; text-only members receive a textual placeholder.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the file.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public ICouncilBuilder WithAttachment(string filePath)
    {
       ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
       _attachments.Add(new FileAttachment(filePath));
       return this;
    }

    /// <summary>
    ///    Attaches a file with a human-readable description. The description is shown
    ///    to text-only members that cannot process binary attachments.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the file.</param>
    /// <param name="description">Human-readable description (e.g. "System Architecture diagram").</param>
    /// <returns>This builder for fluent chaining.</returns>
    public ICouncilBuilder WithAttachment(string filePath, string description)
    {
       ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
       ArgumentException.ThrowIfNullOrWhiteSpace(description);
       _attachments.Add(new FileAttachment(filePath, description));
       return this;
    }

    /// <summary>
    ///    Registers a custom <see cref="IFileContentReader"/> for a specific file
    ///    extension (e.g. <c>.pdf</c>). Overwrites any existing reader for the extension.
    /// </summary>
    /// <param name="extension">File extension including the leading dot (e.g. ".pdf").</param>
    /// <param name="reader">Reader instance.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public ICouncilBuilder WithFileReader(string extension, IFileContentReader reader)
    {
       ArgumentException.ThrowIfNullOrWhiteSpace(extension);
       ArgumentNullException.ThrowIfNull(reader);
       _fileReaders.Register(extension, reader);
       return this;
    }

    /// <summary>
    ///    Registers a delegate-based reader for a specific file extension. The delegate
    ///    is wrapped in a delegate adapter — no class needed.
    ///    class needed.
    /// </summary>
    /// <param name="extension">File extension including the leading dot.</param>
    /// <param name="handler">Delegate that reads the file and returns a <see cref="FileReadResult"/>.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public ICouncilBuilder WithFileReader(string extension, Func<string, CancellationToken, Task<FileReadResult>> handler)
    {
       ArgumentException.ThrowIfNullOrWhiteSpace(extension);
       ArgumentNullException.ThrowIfNull(handler);
       _fileReaders.Register(extension, handler);
       return this;
    }

   // ── Options (bulk configuration) ──

   /// <inheritdoc />
   public ICouncilBuilder WithOptions(CouncilOptions options)
   {
      ArgumentNullException.ThrowIfNull(options);
      ApplyOptions(options);
      return this;
   }

   /// <inheritdoc />
   public ICouncilBuilder WithOptions(Action<CouncilOptions> configure)
   {
      ArgumentNullException.ThrowIfNull(configure);
      var options = new CouncilOptions();
      configure(options);
      ApplyOptions(options);
      return this;
   }

   /// <summary>
   ///    Gets the system prompt for the council.
   /// </summary>
   /// <returns>The system prompt, or <c>null</c> if not set.</returns>
   public string? GetSystemPrompt()
   {
      return _systemPrompt;
   }

   /// <inheritdoc />
   ICouncilExecutor ICouncilBuilder.Build()
   {
      return Build();
   }

   /// <summary>
   ///    Applies a <see cref="CouncilOptions" /> snapshot to the builder.
   ///    Only sets fields that have non-default values — explicit builder calls
   ///    made before or after <c>WithOptions</c> take precedence.
   /// </summary>
   private void ApplyOptions(CouncilOptions options)
   {
      // Strategy
      if (!string.Equals(options.Strategy, DefaultStrategy, StringComparison.OrdinalIgnoreCase))
         _strategy = options.Strategy.ToLowerInvariant() switch
         {
            "critique" => new CritiqueDebate(),
            "consensus" => new ConsensusDebate(),
            _ => new StandardDebate()
         };

      // Core parameters
      if (options.MaxRounds != DefaultMaxRounds) _maxRounds = options.MaxRounds;
      if (Math.Abs(options.Temperature - DefaultTemperature) > 0.001f) _temperature = options.Temperature;
      if (options.SystemPrompt is { Length: > 0 } sp && sp != DefaultSystemPrompt)
         _systemPrompt = sp;
      if (options.ResponseLanguage is { Length: > 0 } lang)
         _responseLanguage = lang;
      if (options.MaxDegreeOfParallelism > 0)
         _maxDegreeOfParallelism = options.MaxDegreeOfParallelism;

      // Compression
      if (options.Compression is { Enabled: true })
      {
         if (_compressor is null)
            _compressor = CompressionFactory.Create(
               options.Compression.Strategy,
               null,
               null,
               null);

         _compressionOptions ??= new CompressionOptions
         {
            TargetRatio = options.Compression.TargetRatio
         };

         if (options.Compression.EnableCache && _compressionCache is null)
            _compressionCache = new CompressionCache(options.Compression.MaxCacheEntries);
      }

      // AutoChunking
      if (options.AutoChunking is { Enabled: true } && _autoChunkingOptions is null)
         _autoChunkingOptions = options.AutoChunking.ToOptions();

      // Telemetry
      if (options.Telemetry is { Enabled: true } && _telemetryOptions is null)
         _telemetryOptions = options.Telemetry;

      // Output
      if (options.Output is { Directory: { Length: > 0 } dir } && dir != "./debate_results")
         _outputPath = dir;
   }

   /// <summary>Creates and attaches a Knowledge Keeper from a RAG provider, model and collection.</summary>
   public ICouncilBuilder WithKnowledgeKeeper(
      IRagProvider ragProvider,
      string modelName,
      ILLMProvider llmProvider,
      string collectionName = "council_knowledge")
   {
      ArgumentNullException.ThrowIfNull(ragProvider);
      ArgumentNullException.ThrowIfNull(llmProvider);
      var member = new CouncilMember(modelName, llmProvider, "Knowledge Keeper");
      _knowledgeKeeper = new KnowledgeKeeper(ragProvider, member, collectionName);
      return this;
   }

   // ── Build ──

   /// <summary>
   ///    Validates configuration and builds a <see cref="CouncilExecutor" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">When required configuration is missing.</exception>
   public CouncilExecutor Build()
   {
      if (_members.Count == 0)
         throw new InvalidOperationException("Council must have at least one member. Use AddMember().");
      if (string.IsNullOrWhiteSpace(_userPrompt))
         throw new InvalidOperationException("User prompt is required. Use WithUserPrompt().");
      if (_maxParticipants is { } limit && _members.Count > limit)
         throw new InvalidOperationException(
            $"Participant limit exceeded: {_members.Count} members added, but limit is {limit}. " +
            "Use WithParticipantLimit() to raise the limit or remove members.");

      var context = new PromptContext
      {
         SystemPrompt = _systemPrompt,
         UserPrompt = _userPrompt,
         KnowledgeContent = _knowledgeBase?.GetAllContent(),
         KnowledgeFiles = _knowledgeBase?.GetLoadedSources().ToList() ?? []
      };

      // Build the deferred Operator (if configured via the convenience overload) so it can reuse
      // the council's compressor when requested.
      var @operator = _operator;
      if (@operator is null && _operatorModel is not null && _operatorServers is not null)
      {
         var clients = _operatorServers.Select(s => (IMcpClient)new McpClientAdapter(s)).ToList();
         @operator = new Operator(
            _operatorModel,
            clients,
            _operatorReuseCompression
               ? _compressor
               : null,
            _operatorReuseCompression
               ? _compressionOptions
               : null);
      }

      var executionOptions = new DebateExecutionOptions(
         _responseLanguage,
         _maxDegreeOfParallelism,
         _logger);

      return new CouncilExecutor(
         _members.AsReadOnly(),
         _chairman,
         _knowledgeKeeper,
         _strategy,
         context,
         _maxRounds,
         _temperature,
         _outputPath,
         _compressor,
         _compressionOptions,
         _compressionCache,
         @operator,
         executionOptions,
         _autoChunkingOptions,
         _telemetryOptions,
         _debateTimeout,
         _strategySelector,
         _votingStrategy,
         _structuredOutputSerializer,
         _structuredOutputType,
         _debateStore,
         _resumeFromDebateId,
         _agentMemory,
          _attachments.AsReadOnly(),
          _fileReaders,
          _cacheBehavior,
          _cache);
   }
}
