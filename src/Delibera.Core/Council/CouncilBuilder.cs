using Delibera.Core.Compression;
using Delibera.Core.Debate;
using Delibera.Core.Providers.Mcp;
using Microsoft.Extensions.Logging;

namespace Delibera.Core.Council;

/// <summary>
///    Fluent builder for assembling and configuring a council debate session.
/// </summary>
public sealed class CouncilBuilder : ICouncilBuilder
{
   private readonly List<CouncilMember> _members = [];
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
       _responseLanguage = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
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
          ResponseLanguage: _responseLanguage,
          MaxDegreeOfParallelism: _maxDegreeOfParallelism,
          Logger: _logger);

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
          executionOptions);
    }
}