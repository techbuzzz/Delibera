using Delibera.Core.Compression;
using Delibera.Core.Debate;

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
   private int _maxRounds = 4;
   private string? _outputPath;
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

   /// <summary>Backward-compatible alias for <see cref="SetChairman(CouncilMember)" />.</summary>
   [Obsolete("Use SetChairman instead.")]
   public ICouncilBuilder SetModerator(CouncilMember moderator) => SetChairman(moderator);

   /// <summary>Backward-compatible alias for <see cref="SetChairman(string, ILLMProvider, string?)" />.</summary>
   [Obsolete("Use SetChairman instead.")]
   public ICouncilBuilder SetModerator(string modelName, ILLMProvider provider, string? persona = null)
      => SetChairman(modelName, provider, persona);

   // ── Knowledge Keeper ──

   /// <inheritdoc />
   public ICouncilBuilder WithKnowledgeKeeper(KnowledgeKeeper knowledgeKeeper)
   {
      _knowledgeKeeper = knowledgeKeeper ?? throw new ArgumentNullException(nameof(knowledgeKeeper));
      return this;
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
         _compressionCache);
   }

   /// <inheritdoc />
   ICouncilExecutor ICouncilBuilder.Build() => Build();
}
