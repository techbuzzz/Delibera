using Delibera.Core.Chunking;
using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.DependencyInjection;

namespace Delibera.Core.Interfaces;

/// <summary>
///    Fluent interface for assembling and configuring a council debate session.
///    Enables dependency injection and testability of council construction.
/// </summary>
/// <remarks>
///    All members return <see cref="ICouncilBuilder" /> to support both the concrete
///    <c>CouncilBuilder</c> and any custom in-process implementation without
///    covariance friction.
/// </remarks>
public interface ICouncilBuilder
{
   /// <summary>Adds a participant to the council.</summary>
   /// <param name="member">Council member to add.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder AddMember(CouncilMember member);

   /// <summary>Adds a participant by model name and provider.</summary>
   /// <param name="modelName">Model name.</param>
   /// <param name="provider">LLM provider instance.</param>
   /// <param name="role">Optional role label.</param>
   /// <param name="persona">Optional persona description.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder AddMember(string modelName, ILLMProvider provider, string? role = null, string? persona = null);

   /// <summary>Assigns the debate Chairman.</summary>
   /// <param name="chairman">Chairman member.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder SetChairman(CouncilMember chairman);

   /// <summary>Assigns a Chairman by model and provider.</summary>
   /// <param name="modelName">Model name.</param>
   /// <param name="provider">LLM provider.</param>
   /// <param name="persona">Optional persona.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder SetChairman(string modelName, ILLMProvider provider, string? persona = null);

   /// <summary>Attaches a Knowledge Keeper with a RAG provider.</summary>
   /// <param name="knowledgeKeeper">Configured Knowledge Keeper instance.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithKnowledgeKeeper(KnowledgeKeeper knowledgeKeeper);

   /// <summary>Attaches a pre-configured Operator (MCP tool micro-agent).</summary>
   /// <param name="operator">Configured Operator instance.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithOperator(Operator @operator);

   /// <summary>
   ///    Creates and attaches an Operator from a (cheaper) model and one or more MCP server
   ///    configurations. The Operator connects to the servers, discovers their tools, and lets
   ///    participants delegate natural-language tasks to it during the debate.
   /// </summary>
   /// <param name="modelName">Model name used by the Operator (typically a cheaper model).</param>
   /// <param name="provider">LLM provider for the Operator model.</param>
   /// <param name="servers">MCP server configurations the Operator connects to.</param>
   /// <param name="reuseCompression">
   ///    When <c>true</c> (default), the Operator reuses the council's configured compressor
   ///    (if any) to compress large tool results before returning them.
   /// </param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithOperator(
      string modelName,
      ILLMProvider provider,
      IEnumerable<McpServerConfig> servers,
      bool reuseCompression = true);

   /// <summary>Sets the debate strategy.</summary>
   /// <param name="strategy">Strategy implementation.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithStrategy(IDebateStrategy strategy);

   /// <summary>Uses the standard 4-round debate strategy.</summary>
   ICouncilBuilder WithStandardDebate()
   {
      return WithStrategy(new StandardDebate());
   }

   /// <summary>Uses the adversarial critique debate strategy.</summary>
   ICouncilBuilder WithCritiqueDebate()
   {
      return WithStrategy(new CritiqueDebate());
   }

   /// <summary>Uses the consensus-building debate strategy.</summary>
   ICouncilBuilder WithConsensusDebate()
   {
      return WithStrategy(new ConsensusDebate());
   }

   /// <summary>Attaches a legacy knowledge base for prompt injection.</summary>
   /// <param name="knowledgeBase">Knowledge base instance.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithKnowledge(IKnowledgeBase knowledgeBase);

   /// <summary>Enables context compression with the specified compressor.</summary>
   /// <param name="compressor">Compressor implementation.</param>
   /// <param name="options">Optional compression options.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithCompression(IContextCompressor compressor, CompressionOptions? options = null);

   /// <summary>Enables context compression by strategy type.</summary>
   /// <param name="strategy">Compression strategy.</param>
   /// <param name="llmProvider">LLM provider (for Summarization/Hybrid).</param>
   /// <param name="modelName">Model name (for Summarization/Hybrid).</param>
   /// <param name="embeddingProvider">Embedding provider (for Semantic/Hybrid).</param>
   /// <param name="options">Compression options.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithCompression(
      CompressionStrategy strategy,
      ILLMProvider? llmProvider = null,
      string? modelName = null,
      IEmbeddingProvider? embeddingProvider = null,
      CompressionOptions? options = null);

   /// <summary>Sets custom compression options.</summary>
   /// <param name="options">Compression options.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithCompressionOptions(CompressionOptions options);

   /// <summary>Enables compression result caching.</summary>
   /// <param name="maxEntries">Maximum cache entries.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithCompressionCache(int maxEntries = 256);

   /// <summary>Sets the system prompt shared by all models.</summary>
   /// <param name="systemPrompt">System prompt text.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithSystemPrompt(string systemPrompt);

   /// <summary>Sets the user prompt (the question or task).</summary>
   /// <param name="userPrompt">User prompt text.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithUserPrompt(string userPrompt);

   /// <summary>Sets the maximum number of debate rounds (1–10).</summary>
   /// <param name="maxRounds">Max rounds.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithMaxRounds(int maxRounds);

   /// <summary>Sets the generation temperature (0.0–2.0).</summary>
   /// <param name="temperature">Temperature value.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithTemperature(float temperature);

   /// <summary>Sets the output path for saving the debate result as Markdown.</summary>
   /// <param name="outputPath">File path for Markdown output.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder SaveResultTo(string outputPath);

   /// <summary>
   ///    Forces every model response (participants, Chairman, Knowledge Keeper, Operator)
   ///    to be in the specified language. Pass <c>null</c> or empty to disable language
   ///    enforcement and let the model pick a language from context.
   /// </summary>
   /// <param name="language">
   ///    Language name the model recognises (e.g. "Russian", "English", "Spanish").
   /// </param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithResponseLanguage(string? language);

   /// <summary>
   ///    Sets the maximum degree of parallelism for operations that can run concurrently
   ///    within a debate round (Operator task delegation, parallel Knowledge Keeper
   ///    queries). Pass <c>0</c> for unbounded parallelism (default).
   /// </summary>
   /// <param name="maxDegreeOfParallelism">Max concurrent operations per round (0 = unbounded).</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithMaxDegreeOfParallelism(int maxDegreeOfParallelism);

   /// <summary>
   ///    Attaches an <see cref="ILogger" /> used by the executor to surface progress
   ///    (Chairman actions, rounds, compression, errors, …) to a host's logging pipeline.
   ///    Pass <c>null</c> to disable structured logging (legacy behaviour — only the
   ///    <c>OnLog</c> event and the <see cref="ExecutionLog" /> collection are populated).
   /// </summary>
   /// <param name="logger">Logger instance, or <c>null</c> to clear.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithLogger(ILogger? logger);

   /// <summary>
   ///    Enables AutoChunking — automatic splitting of large knowledge documents into
   ///    context-window-sized chunks distributed across debate rounds.
   /// </summary>
   /// <remarks>
   ///    <para>
   ///       When enabled, the orchestrator queries each model's context window size
   ///       (via <see cref="ILLMProvider.GetModelCapabilitiesAsync" /> or the
   ///       <see cref="ModelContextWindowRegistry" /> fallback) and creates a
   ///       <see cref="ChunkingPlan" /> if the knowledge content exceeds the smallest
   ///       model's capacity. Chunks are progressively disclosed across rounds so
   ///       every model receives a complete view of the document by the final round.
   ///    </para>
   ///    <para>
   ///       Use <see cref="WithModelContextWindow" /> to register custom model context
   ///       window sizes that are not in the built-in registry.
   ///    </para>
   /// </remarks>
   /// <param name="options">
   ///    Chunking configuration. Pass <c>null</c> to use <see cref="AutoChunkingOptions.Default" />.
   /// </param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithAutoChunking(AutoChunkingOptions? options = null);

   /// <summary>
   ///    Registers a custom context window size for a model pattern.
   ///    The pattern is matched case-insensitively as a substring of the model name.
   /// </summary>
   /// <param name="modelNamePattern">
   ///    Substring pattern (e.g. "my-fine-tuned-llama" matches "my-fine-tuned-llama:v2").
   /// </param>
   /// <param name="contextWindowTokens">Context window size in tokens.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithModelContextWindow(string modelNamePattern, int contextWindowTokens);

   /// <summary>
   ///    Applies a pre-built <see cref="CouncilOptions" /> snapshot to the builder.
   ///    All non-default values are transferred. Explicit builder calls made before
   ///    or after this method take precedence over the options snapshot.
   /// </summary>
   /// <param name="options">Configuration to apply.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithOptions(CouncilOptions options);

   /// <summary>
   ///    Applies configuration via a delegate that receives a fresh
   ///    <see cref="CouncilOptions" /> instance. Useful for inline configuration
   ///    without a separate options object.
   /// </summary>
   /// <param name="configure">Delegate that populates the options.</param>
   /// <returns>This builder for fluent chaining.</returns>
   /// <example>
   /// <code>
   /// builder.WithOptions(o =>
   /// {
   ///     o.Strategy = "Critique";
   ///     o.MaxRounds = 6;
   ///     o.AutoChunking.Enabled = true;
   /// });
   /// </code>
   /// </example>
   ICouncilBuilder WithOptions(Action<CouncilOptions> configure);

   /// <summary>
   ///    Validates configuration and builds an <see cref="ICouncilExecutor" />.
   /// </summary>
   /// <returns>Configured council executor ready for debate execution.</returns>
   /// <exception cref="InvalidOperationException">When required configuration is missing.</exception>
   ICouncilExecutor Build();
}