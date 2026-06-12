namespace Delibera.Core.Council;

/// <summary>
/// Special council role — the Knowledge Keeper.
/// Connects to a RAG provider and uses a dedicated LLM model to answer
/// fact-based queries during debate rounds.
/// </summary>
public sealed class KnowledgeKeeper
{
   private readonly IRagProvider _ragProvider;
   private readonly CouncilMember _model;
   private readonly string _collectionName;
   private readonly List<KnowledgeInteraction> _interactions = [];

   /// <summary>Display name shown in debate logs.</summary>
   public string DisplayName => $"📚 Knowledge Keeper ({_model.ModelName})";

   /// <summary>RAG collection used for searching.</summary>
   public string CollectionName => _collectionName;

   /// <summary>All interactions recorded during this session.</summary>
   public IReadOnlyList<KnowledgeInteraction> Interactions => _interactions.AsReadOnly();

   /// <summary>
   /// Creates a Knowledge Keeper.
   /// </summary>
   /// <param name="ragProvider">RAG provider for vector search.</param>
   /// <param name="model">Dedicated LLM council member for generating answers.</param>
   /// <param name="collectionName">Vector collection to search against.</param>
   public KnowledgeKeeper(IRagProvider ragProvider, CouncilMember model, string collectionName)
   {
      _ragProvider = ragProvider ?? throw new ArgumentNullException(nameof(ragProvider));
      _model = model ?? throw new ArgumentNullException(nameof(model));
      _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
   }

   /// <summary>
   /// Performs a semantic search against the knowledge base.
   /// </summary>
   /// <param name="query">Natural language query.</param>
   /// <param name="limit">Maximum number of chunks to retrieve.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Scored search results from the vector store.</returns>
   public async Task<IReadOnlyList<VectorSearchResult>> SearchKnowledgeAsync(
       string query, int limit = 5, CancellationToken ct = default)
   {
      return await _ragProvider.SearchAsync(_collectionName, query, limit, ct: ct);
   }

   /// <summary>
   /// Generates an answer to a question using RAG context.
   /// Searches for relevant chunks, injects them into the prompt, and asks the model.
   /// </summary>
   /// <param name="question">The question to answer.</param>
   /// <param name="limit">Maximum number of context chunks to use.</param>
   /// <param name="temperature">Generation temperature.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The Knowledge Keeper's answer.</returns>
   public async Task<string> AnswerQuestionAsync(
       string question,
       int limit = 5,
       float temperature = 0.3f,
       CancellationToken ct = default)
   {
      // 1. Retrieve context from RAG
      var context = await _ragProvider.GetContextAsync(_collectionName, question, limit, ct);

      var systemPrompt = """
            You are the Knowledge Keeper — a librarian and fact-checker for an AI council debate.
            Your role is to provide accurate, well-sourced answers based ONLY on the context provided.
            If the context does not contain sufficient information, clearly state what you know
            and what is uncertain. Always cite which source chunks support your answer.
            Be concise and factual.
            """;

      string userPrompt;
      int sourceChunks;

      if (string.IsNullOrWhiteSpace(context))
      {
         userPrompt = $"""
                Question: {question}
                
                No relevant documents were found in the knowledge base.
                Please state that you have no relevant context and provide
                whatever general knowledge you can, clearly marking it as
                "general knowledge" rather than sourced information.
                """;
         sourceChunks = 0;
      }
      else
      {
         userPrompt = $"""
                ### Retrieved Context:
                {context}

                ### Question:
                {question}

                Provide a clear, factual answer based on the context above.
                Cite relevant source numbers in your answer.
                """;
         sourceChunks = context.Split("[Source ").Length - 1;
      }

      // 2. Generate answer via dedicated LLM
      var answer = await _model.AskAsync(systemPrompt, userPrompt, temperature, ct);

      // 3. Log the interaction
      _interactions.Add(new KnowledgeInteraction(question, answer, sourceChunks));

      return answer;
   }

   /// <summary>
   /// Answers a question using pre-fetched search results (useful when the caller
   /// has already performed a search and wants to reuse the results).
   /// </summary>
   public async Task<string> AnswerWithContextAsync(
       string question,
       IReadOnlyList<VectorSearchResult> searchResults,
       float temperature = 0.3f,
       CancellationToken ct = default)
   {
      var sb = new StringBuilder();
      for (var i = 0; i < searchResults.Count; i++)
      {
         sb.AppendLine($"[Source {i + 1} — score: {searchResults[i].Score:F3}]");
         sb.AppendLine(searchResults[i].Text);
         sb.AppendLine();
      }

      var systemPrompt = """
            You are the Knowledge Keeper — a librarian and fact-checker for an AI council debate.
            Provide accurate answers based ONLY on the context provided. Cite source numbers.
            """;

      var userPrompt = $"""
            ### Retrieved Context:
            {sb}

            ### Question:
            {question}
            """;

      var answer = await _model.AskAsync(systemPrompt, userPrompt, temperature, ct);
      _interactions.Add(new KnowledgeInteraction(question, answer, searchResults.Count));
      return answer;
   }

   /// <summary>
   /// Provides structured context for a specific debate round.
   /// Queries the knowledge base with the round topic and previous round summaries,
   /// returning a formatted response with sources.
   /// </summary>
   /// <param name="topic">The debate topic or current round question.</param>
   /// <param name="roundNumber">Current round number.</param>
   /// <param name="previousRoundSummary">Optional summary of previous rounds to refine the query.</param>
   /// <param name="limit">Maximum number of source chunks to retrieve.</param>
   /// <param name="temperature">Generation temperature.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>A structured knowledge response with sources and the interaction record.</returns>
   public async Task<KnowledgeRoundContext> ProvideContextForRoundAsync(
       string topic,
       int roundNumber,
       string? previousRoundSummary = null,
       int limit = 5,
       float temperature = 0.3f,
       CancellationToken ct = default)
   {
      // Build a refined query incorporating round context
      var query = roundNumber <= 1
          ? topic
          : $"{topic}\n\nContext from previous rounds:\n{previousRoundSummary ?? "(none)"}";

      // Search for relevant chunks
      var searchResults = await _ragProvider.SearchAsync(_collectionName, query, limit, ct: ct);
      var sources = searchResults.Select(r => new KnowledgeSource(
          Text: r.Text,
          Score: r.Score,
          Metadata: r.Metadata ?? new Dictionary<string, string>()
      )).ToList();

      // Generate the answer
      var systemPrompt = $"""
            You are the Knowledge Keeper providing context for Round {roundNumber} of a council debate.
            Your role is to surface the most relevant facts and evidence from the knowledge base.
            
            Structure your response as:
            1. **Key Facts** — bullet points of the most relevant facts
            2. **Evidence** — specific quotes or data from sources
            3. **Relevance** — how this context relates to the current discussion point
            
            Be concise, factual, and cite source numbers.
            """;

      var contextText = await _ragProvider.GetContextAsync(_collectionName, query, limit, ct);
      string answer;

      if (string.IsNullOrWhiteSpace(contextText))
      {
         answer = $"No relevant documents found in knowledge base for Round {roundNumber}.";
      }
      else
      {
         var userPrompt = $"""
                ### Retrieved Context:
                {contextText}

                ### Debate Topic:
                {topic}

                ### Round {roundNumber} — Provide structured knowledge context.
                """;

         answer = await _model.AskAsync(systemPrompt, userPrompt, temperature, ct);
      }

      var interaction = new KnowledgeInteraction(query, answer, sources.Count);
      _interactions.Add(interaction);

      return new KnowledgeRoundContext(
          RoundNumber: roundNumber,
          Answer: answer,
          Sources: sources.AsReadOnly(),
          Interaction: interaction);
   }

   /// <summary>
   /// Indexes a document into the Knowledge Keeper's collection.
   /// Convenience wrapper around <see cref="IRagProvider.IndexDocumentAsync"/>.
   /// </summary>
   public Task<int> IndexDocumentAsync(
       string documentText,
       Dictionary<string, string>? metadata = null,
       int chunkSize = 500,
       int chunkOverlap = 50,
       CancellationToken ct = default) =>
       _ragProvider.IndexDocumentAsync(_collectionName, documentText, metadata, chunkSize, chunkOverlap, ct);

   /// <summary>
   /// Indexes a file into the Knowledge Keeper's collection.
   /// </summary>
   public Task<int> IndexFileAsync(
       string filePath,
       int chunkSize = 500,
       int chunkOverlap = 50,
       CancellationToken ct = default) =>
       _ragProvider.IndexFileAsync(_collectionName, filePath, chunkSize, chunkOverlap, ct);
}
