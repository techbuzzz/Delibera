using Delibera.Core.Chunking;
using Delibera.Core.Compression;
using Delibera.Core.Debate;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Providers.Mcp;

namespace Delibera.Core.Council;

/// <summary>
///    Fluent builder for assembling and configuring a council debate session.
/// </summary>
public sealed class CouncilBuilder : ICouncilBuilder
{
   private readonly List<CouncilMember> _members = [];
   private AutoChunkingOptions? _autoChunkingOptions;
   private CouncilMember? _chairman;
   private CompressionCache? _compressionCache;
   private CompressionOptions? _compressionOptions;
   private IContextCompressor? _compressor;
   private IKnowledgeBase? _knowledgeBase;
   private KnowledgeKeeper? _knowledgeKeeper;
   private ILogger? _logger;
   private int _maxDegreeOfParallelism;
   private int _maxRounds = 4;
   private Operator? _operator;
   private CouncilMember? _operatorModel;
   private bool _operatorReuseCompression;
   private IReadOnlyList<McpServerConfig>? _operatorServers;
   private string? _outputPath;
   private string? _responseLanguage;
   private IDebateStrategy _strategy = new StandardDebate();
   private string _systemPrompt = "You are a helpful AI assistant participating in a council debate.";
   private float _temperature = 0.7f;
   private string _userPrompt = string.Empty;

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
      _members.Add(new CouncilMember(modelName, provider, role, persona));
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
   ///    Applies a <see cref="CouncilOptions" /> snapshot to the builder.
   ///    Only sets fields that have non-default values — explicit builder calls
   ///    made before or after <c>WithOptions</c> take precedence.
   /// </summary>
   private void ApplyOptions(CouncilOptions options)
   {
      // Strategy
      if (!string.Equals(options.Strategy, "Standard", StringComparison.OrdinalIgnoreCase))
      {
         _strategy = options.Strategy.ToLowerInvariant() switch
         {
            "critique" => new CritiqueDebate(),
            "consensus" => new ConsensusDebate(),
            _ => new StandardDebate()
         };
      }

      // Core parameters
      if (options.MaxRounds != 4) _maxRounds = options.MaxRounds;
      if (Math.Abs(options.Temperature - 0.7f) > 0.001f) _temperature = options.Temperature;
      if (options.SystemPrompt is { Length: > 0 } sp && sp != "You are a helpful AI assistant participating in a council debate.")
         _systemPrompt = sp;
      if (options.ResponseLanguage is { Length: > 0 } lang)
         _responseLanguage = lang;
      if (options.MaxDegreeOfParallelism > 0)
         _maxDegreeOfParallelism = options.MaxDegreeOfParallelism;

      // Compression
      if (options.Compression is { Enabled: true })
      {
         if (_compressor is null)
         {
            _compressor = CompressionFactory.Create(
               options.Compression.Strategy,
               llmProvider: null,
               modelName: null,
               embeddingProvider: null);
         }

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

      // Output
      if (options.Output is { Directory: { Length: > 0 } dir } && dir != "./debate_results")
         _outputPath = dir;
   }

   /// <inheritdoc />
   ICouncilExecutor ICouncilBuilder.Build()
   {
      return Build();
   }

   /// <summary>Backward-compatible alias for <see cref="SetChairman(CouncilMember)" />.</summary>
   [Obsolete("Use SetChairman instead.")]
   public ICouncilBuilder SetModerator(CouncilMember moderator)
   {
      return SetChairman(moderator);
   }

   /// <summary>Backward-compatible alias for <see cref="SetChairman(string, ILLMProvider, string?)" />.</summary>
   [Obsolete("Use SetChairman instead.")]
   public ICouncilBuilder SetModerator(string modelName, ILLMProvider provider, string? persona = null)
   {
      return SetChairman(modelName, provider, persona);
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
         _autoChunkingOptions);
   }
}