using Delibera.Core.Compression;
using Delibera.Core.Debate;

namespace Delibera.Core.Council;

/// <summary>
/// Fluent builder for assembling and configuring a council debate session.
/// </summary>
public sealed class CouncilBuilder : ICouncilBuilder
{
   private readonly List<CouncilMember> _members = [];
   private CouncilMember? _chairman;
   private KnowledgeKeeper? _knowledgeKeeper;
   private IDebateStrategy _strategy = new StandardDebate();
   private IKnowledgeBase? _knowledgeBase;
   private string _systemPrompt = "You are a helpful AI assistant participating in a council debate.";
   private string _userPrompt = string.Empty;
   private int _maxRounds = 4;
   private float _temperature = 0.7f;
   private string? _outputPath;
   private IContextCompressor? _compressor;
   private CompressionOptions? _compressionOptions;
   private CompressionCache? _compressionCache;

   // ── Members ──

   /// <summary>Adds a participant to the council.</summary>
   public CouncilBuilder AddMember(CouncilMember member)
   {
      _members.Add(member ?? throw new ArgumentNullException(nameof(member)));
      return this;
   }

   /// <summary>Adds a participant by model name and provider.</summary>
   public CouncilBuilder AddMember(string modelName, ILLMProvider provider, string? role = null, string? persona = null)
   {
      _members.Add(new CouncilMember(modelName, provider, role, persona));
      return this;
   }

   // ── Chairman ──

   /// <summary>Assigns the debate Chairman.</summary>
   public CouncilBuilder SetChairman(CouncilMember chairman)
   {
      _chairman = chairman ?? throw new ArgumentNullException(nameof(chairman));
      _chairman.Role = "Chairman";
      return this;
   }

   /// <summary>Assigns a Chairman by model and provider.</summary>
   public CouncilBuilder SetChairman(string modelName, ILLMProvider provider, string? persona = null)
   {
      _chairman = new CouncilMember(modelName, provider, "Chairman", persona);
      return this;
   }

   /// <summary>Backward-compatible alias for <see cref="SetChairman(CouncilMember)"/>.</summary>
   [Obsolete("Use SetChairman instead.")]
   public CouncilBuilder SetModerator(CouncilMember moderator) => SetChairman(moderator);

   /// <summary>Backward-compatible alias for <see cref="SetChairman(string, ILLMProvider, string?)"/>.</summary>
   [Obsolete("Use SetChairman instead.")]
   public CouncilBuilder SetModerator(string modelName, ILLMProvider provider, string? persona = null) =>
       SetChairman(modelName, provider, persona);

   // ── Knowledge Keeper ──

   /// <summary>Attaches a Knowledge Keeper with a RAG provider.</summary>
   public CouncilBuilder WithKnowledgeKeeper(KnowledgeKeeper knowledgeKeeper)
   {
      _knowledgeKeeper = knowledgeKeeper ?? throw new ArgumentNullException(nameof(knowledgeKeeper));
      return this;
   }

   /// <summary>Creates and attaches a Knowledge Keeper from a RAG provider, model and collection.</summary>
   public CouncilBuilder WithKnowledgeKeeper(
       IRagProvider ragProvider,
       string modelName,
       ILLMProvider llmProvider,
       string collectionName = "council_knowledge")
   {
      var member = new CouncilMember(modelName, llmProvider, "Knowledge Keeper");
      _knowledgeKeeper = new KnowledgeKeeper(ragProvider, member, collectionName);
      return this;
   }

   // ── Strategy ──

   /// <summary>Sets the debate strategy.</summary>
   public CouncilBuilder WithStrategy(IDebateStrategy strategy)
   {
      _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
      return this;
   }

   /// <summary>Uses the standard 4-round debate strategy.</summary>
   public CouncilBuilder WithStandardDebate() => WithStrategy(new StandardDebate());

   /// <summary>Uses the adversarial critique debate strategy.</summary>
   public CouncilBuilder WithCritiqueDebate() => WithStrategy(new CritiqueDebate());

   /// <summary>Uses the consensus-building debate strategy.</summary>
   public CouncilBuilder WithConsensusDebate() => WithStrategy(new ConsensusDebate());

   // ── Knowledge base (legacy MD-based) ──

   /// <summary>Attaches a legacy knowledge base for prompt injection.</summary>
   public CouncilBuilder WithKnowledge(IKnowledgeBase knowledgeBase)
   {
      _knowledgeBase = knowledgeBase;
      return this;
   }

   // ── Context Compression ──

   /// <summary>Enables context compression with the specified compressor.</summary>
   /// <param name="compressor">Compressor implementation.</param>
   /// <param name="options">Optional compression options.</param>
   public CouncilBuilder WithCompression(IContextCompressor compressor, CompressionOptions? options = null)
   {
      _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
      _compressionOptions = options;
      return this;
   }

   /// <summary>Enables context compression by strategy type.</summary>
   /// <param name="strategy">Compression strategy to use.</param>
   /// <param name="llmProvider">LLM provider (required for Summarization/Hybrid strategies).</param>
   /// <param name="modelName">Model name (required for Summarization/Hybrid strategies).</param>
   /// <param name="embeddingProvider">Embedding provider (required for Semantic/Hybrid strategies).</param>
   /// <param name="options">Optional compression options.</param>
   public CouncilBuilder WithCompression(
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

   /// <summary>Sets custom compression options (requires compression to be enabled).</summary>
   public CouncilBuilder WithCompressionOptions(CompressionOptions options)
   {
      _compressionOptions = options ?? throw new ArgumentNullException(nameof(options));
      return this;
   }

   /// <summary>Enables compression result caching to avoid re-compressing identical text.</summary>
   /// <param name="maxEntries">Maximum cache entries (default 256).</param>
   public CouncilBuilder WithCompressionCache(int maxEntries = 256)
   {
      _compressionCache = new CompressionCache(maxEntries);
      return this;
   }

   // ── Prompts / parameters ──

   /// <summary>Sets the system prompt shared by all models.</summary>
   public CouncilBuilder WithSystemPrompt(string systemPrompt) { _systemPrompt = systemPrompt; return this; }

   /// <summary>Sets the user prompt (the question or task).</summary>
   public CouncilBuilder WithUserPrompt(string userPrompt) { _userPrompt = userPrompt; return this; }

   /// <summary>Sets the maximum number of debate rounds (1–10).</summary>
   public CouncilBuilder WithMaxRounds(int maxRounds) { _maxRounds = Math.Clamp(maxRounds, 1, 10); return this; }

   /// <summary>Sets the generation temperature (0.0–2.0).</summary>
   public CouncilBuilder WithTemperature(float temperature) { _temperature = Math.Clamp(temperature, 0f, 2f); return this; }

   /// <summary>Sets the output path for saving the debate result as Markdown.</summary>
   public CouncilBuilder SaveResultTo(string outputPath) { _outputPath = outputPath; return this; }

   // ── ICouncilBuilder explicit implementations ──

   ICouncilBuilder ICouncilBuilder.AddMember(CouncilMember member) => AddMember(member);
   ICouncilBuilder ICouncilBuilder.AddMember(string modelName, ILLMProvider provider, string? role, string? persona) => AddMember(modelName, provider, role, persona);
   ICouncilBuilder ICouncilBuilder.SetChairman(CouncilMember chairman) => SetChairman(chairman);
   ICouncilBuilder ICouncilBuilder.SetChairman(string modelName, ILLMProvider provider, string? persona) => SetChairman(modelName, provider, persona);
   ICouncilBuilder ICouncilBuilder.WithKnowledgeKeeper(KnowledgeKeeper knowledgeKeeper) => WithKnowledgeKeeper(knowledgeKeeper);
   ICouncilBuilder ICouncilBuilder.WithStrategy(IDebateStrategy strategy) => WithStrategy(strategy);
   ICouncilBuilder ICouncilBuilder.WithKnowledge(IKnowledgeBase knowledgeBase) => WithKnowledge(knowledgeBase);
   ICouncilBuilder ICouncilBuilder.WithCompression(IContextCompressor compressor, CompressionOptions? options) => WithCompression(compressor, options);
   ICouncilBuilder ICouncilBuilder.WithCompression(CompressionStrategy strategy, ILLMProvider? llmProvider, string? modelName, IEmbeddingProvider? embeddingProvider, CompressionOptions? options) => WithCompression(strategy, llmProvider, modelName, embeddingProvider, options);
   ICouncilBuilder ICouncilBuilder.WithCompressionOptions(CompressionOptions options) => WithCompressionOptions(options);
   ICouncilBuilder ICouncilBuilder.WithCompressionCache(int maxEntries) => WithCompressionCache(maxEntries);
   ICouncilBuilder ICouncilBuilder.WithSystemPrompt(string systemPrompt) => WithSystemPrompt(systemPrompt);
   ICouncilBuilder ICouncilBuilder.WithUserPrompt(string userPrompt) => WithUserPrompt(userPrompt);
   ICouncilBuilder ICouncilBuilder.WithMaxRounds(int maxRounds) => WithMaxRounds(maxRounds);
   ICouncilBuilder ICouncilBuilder.WithTemperature(float temperature) => WithTemperature(temperature);
   ICouncilBuilder ICouncilBuilder.SaveResultTo(string outputPath) => SaveResultTo(outputPath);

   // ── Build ──

   /// <summary>
   /// Validates configuration and builds a <see cref="CouncilExecutor"/>.
   /// </summary>
   /// <exception cref="InvalidOperationException">When required configuration is missing.</exception>
   public CouncilExecutor Build()
   {
      return BuildInternal();
   }

   /// <inheritdoc/>
   ICouncilExecutor ICouncilBuilder.Build() => BuildInternal();

   private CouncilExecutor BuildInternal()
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

      return new CouncilExecutor(
          members: _members.AsReadOnly(),
          chairman: _chairman,
          knowledgeKeeper: _knowledgeKeeper,
          strategy: _strategy,
          context: context,
          maxRounds: _maxRounds,
          temperature: _temperature,
          outputPath: _outputPath,
          compressor: _compressor,
          compressionOptions: _compressionOptions,
          compressionCache: _compressionCache);
   }
}
