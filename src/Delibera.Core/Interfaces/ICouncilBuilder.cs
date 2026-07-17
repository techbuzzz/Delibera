using Delibera.Core.Attachments;
using Delibera.Core.Chunking;
using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Memory;
using Delibera.Core.Output;
using Delibera.Core.Persistence;
using Delibera.Core.Telemetry;
using Delibera.Core.Voting;

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
   ///    Enables OpenTelemetry-style observability. When enabled, the
   ///    <see cref="Council.CouncilExecutor" /> emits <see cref="System.Diagnostics.Activity" />
   ///    spans via <see cref="DeliberaActivitySource" /> and records metrics via
   ///    <see cref="DeliberaMeter" />. See <see cref="TelemetryOptions" /> for the
   ///    activity-source / meter naming convention.
   /// </summary>
   /// <param name="options">
   ///    Telemetry configuration. Pass <c>null</c> to use defaults
   ///    (<see cref="TelemetryOptions.Enabled" /> = <c>true</c>, default source/meter names).
   /// </param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithTelemetry(TelemetryOptions? options = null);

   /// <summary>
   ///    Enables OpenTelemetry-style observability with a configuration delegate.
   /// </summary>
   /// <param name="configure">Delegate that populates a fresh <see cref="TelemetryOptions" />.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithTelemetry(Action<TelemetryOptions> configure);

   /// <summary>
   ///    Sets a hard wall-clock timeout for the whole debate. When the timeout
   ///    elapses, the internal <c>CancellationTokenSource</c> used by
   ///    <see cref="ICouncilExecutor.ExecuteAsync(CancellationToken)" /> is cancelled,
   ///    which propagates <see cref="OperationCanceledException" /> through every
   ///    downstream async operation.
   /// </summary>
   /// <param name="timeout">Maximum debate duration. <see cref="Timeout.InfiniteTimeSpan" /> disables.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithTimeout(TimeSpan timeout);

   /// <summary>
   ///    Caps the maximum number of council participants. <see cref="ICouncilBuilder.Build" />
   ///    throws <see cref="InvalidOperationException" /> if more members have been added
   ///    than the limit.
   /// </summary>
   /// <param name="maxParticipants">Maximum allowed participants (must be ≥ 1).</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithParticipantLimit(int maxParticipants);

   /// <summary>
   ///    Enables adaptive strategy switching (F-09). After each round,
   ///    <see cref="ICouncilExecutor" /> calls
   ///    <see cref="IStrategySelector.SelectNextAsync" />; if it returns a non-null
   ///    strategy, the executor swaps <see cref="ICouncilExecutor.Strategy" /> before
   ///    the next round. Use <see cref="AdaptiveStrategySelector" /> for the built-in
   ///    stalemate detector.
   /// </summary>
   /// <param name="selector">The strategy selector to consult after each round.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithAdaptiveStrategy(IStrategySelector selector);

   /// <summary>
   ///    Configures a voting Chairman (F-02) that uses an <see cref="IVotingStrategy" />
   ///    to reach a decision via structured voting among participants, as an
   ///    alternative to single-LLM Chairman synthesis.
   /// </summary>
   /// <param name="modelName">Chairman model name.</param>
   /// <param name="provider">LLM provider.</param>
   /// <param name="votingStrategy">Voting strategy (Majority, BordaCount, Weighted).</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithVotingChairman(string modelName, ILLMProvider provider, IVotingStrategy votingStrategy);

   /// <summary>
   /// Configures a voting strategy (F-02) that uses an <see cref="IVotingStrategy" />
   /// </summary>
   /// <param name="votingStrategy"></param>
   /// <returns></returns>
   ICouncilBuilder WithVoting(IVotingStrategy votingStrategy);

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
   ICouncilBuilder WithStructuredOutput<TVerdict>(IStructuredOutputSerializer? serializer = null) where TVerdict : class;

   /// <summary>
   ///    Attaches an <see cref="IDebateStore" /> so a checkpoint is saved after every
   ///    round (F-03). The debate can be resumed from the last completed round via
   ///    <see cref="ResumeFrom" />.
   /// </summary>
   /// <param name="store">The store to persist checkpoints to.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithPersistence(IDebateStore store);

   /// <summary>
   ///    Resumes a debate from the given <paramref name="debateId" /> (F-03). The
   ///    corresponding checkpoint must exist in the configured
   ///    <see cref="IDebateStore" />.
   /// </summary>
   /// <param name="debateId">The debate identifier to resume.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder ResumeFrom(string debateId);

   /// <summary>
   ///    Sets the caching behavior for this debate. When an <see cref="IDebateCache" />
   ///    is registered in DI, the executor will check the cache before running a debate
   ///    and store results after completion according to the specified behavior.
   /// </summary>
   /// <param name="behavior">Cache behavior (<see cref="CacheBehavior" />).</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithCacheBehavior(CacheBehavior behavior);

   /// <summary>
   ///    Attaches an <see cref="IAgentMemory" /> (F-04) so council members can recall
   ///    context from previous sessions and persist their conclusions after each
   ///    debate. Default is <see cref="InMemoryAgentMemory" /> (no persistence).
   /// </summary>
   /// <param name="memory">Memory backend. <c>null</c> uses <see cref="InMemoryAgentMemory" />.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithAgentMemory(IAgentMemory? memory = null);

   // ── Multi-Modal attachments (F-06) ──

   /// <summary>
   ///    Adds a participant with explicit <see cref="MemberCapabilities"/>. When
   ///    <see cref="MemberCapabilities.Vision"/> is set, the member receives image
   ///    attachments as <c>ImageContent</c> via Microsoft.Extensions.AI.
   /// </summary>
   /// <param name="modelName">Model name (e.g. "llava:13b").</param>
   /// <param name="provider">LLM provider instance.</param>
   /// <param name="role">Role label.</param>
   /// <param name="capabilities">Member capabilities (Text, Vision).</param>
   /// <param name="persona">Optional persona prompt.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder AddMember(string modelName, ILLMProvider provider, string role,
      MemberCapabilities capabilities, string? persona = null);

   /// <summary>
   ///    Attaches a file to the debate. Read lazily by the
   ///    <see cref="FileContentReaderRegistry"/> when the debate starts.
   /// </summary>
   /// <param name="filePath">Path to the file.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithAttachment(string filePath);

   /// <summary>
   ///    Attaches a file with a human-readable description. The description is shown
   ///    to text-only members that cannot process binary attachments.
   /// </summary>
   /// <param name="filePath">Path to the file.</param>
   /// <param name="description">Human-readable description.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithAttachment(string filePath, string description);

   /// <summary>
   ///    Registers a custom <see cref="IFileContentReader"/> for a specific file
   ///    extension (e.g. <c>.pdf</c>).
   /// </summary>
   /// <param name="extension">File extension including the leading dot.</param>
   /// <param name="reader">Reader instance.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithFileReader(string extension, IFileContentReader reader);

   /// <summary>
   ///    Registers a delegate-based reader for a specific file extension.
   /// </summary>
   /// <param name="extension">File extension including the leading dot.</param>
   /// <param name="handler">Delegate that reads the file and returns a <see cref="FileReadResult"/>.</param>
   /// <returns>This builder for fluent chaining.</returns>
   ICouncilBuilder WithFileReader(string extension, Func<string, CancellationToken, Task<FileReadResult>> handler);

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
   ///    <code>
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
   ///    Gets the system prompt for the council.
   /// </summary>
   /// <returns>The system prompt, or <c>null</c> if not set.</returns>
   string? GetSystemPrompt();

   /// <summary>
   ///    Validates configuration and builds an <see cref="ICouncilExecutor" />.
   /// </summary>
   /// <returns>Configured council executor ready for debate execution.</returns>
   /// <exception cref="InvalidOperationException">When required configuration is missing.</exception>
   ICouncilExecutor Build();
}
