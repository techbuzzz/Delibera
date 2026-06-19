using Delibera.Core.Council;
using Delibera.Core.Debate;

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
   ///    Validates configuration and builds an <see cref="ICouncilExecutor" />.
   /// </summary>
   /// <returns>Configured council executor ready for debate execution.</returns>
   /// <exception cref="InvalidOperationException">When required configuration is missing.</exception>
   ICouncilExecutor Build();
}