using Delibera.Core.Providers.RAG;

namespace Delibera.Core.Council;

/// <summary>
///    Special council role — the Knowledge Keeper.
///    Connects to a RAG provider and uses a dedicated LLM model to answer
///    fact-based queries during debate rounds.
/// </summary>
public sealed class KnowledgeKeeper
{
   private readonly List<KnowledgeInteraction> _interactions = [];
   private readonly CouncilMember _model;
   private readonly IRagProvider _ragProvider;

   /// <summary>
   ///    Creates a keeper over a single collection.
   /// </summary>
   public KnowledgeKeeper(IRagProvider ragProvider, CouncilMember model, string collectionName)
      : this(ragProvider, model, [collectionName])
   {
   }

   /// <summary>
   ///    Creates a keeper spanning several collections — one per requested corpus.
   /// </summary>
   /// <remarks>
   ///    A debate may be grounded in several corpora, but a vector store search takes one
   ///    collection at a time, so this searches them all and merges by score. Without it the only
   ///    correct behaviour would be to silently ground the debate in the first corpus and ignore
   ///    the rest.
   /// </remarks>
   public KnowledgeKeeper(IRagProvider ragProvider, CouncilMember model, IEnumerable<string> collectionNames)
   {
      ArgumentNullException.ThrowIfNull(collectionNames);
      _model = model ?? throw new ArgumentNullException(nameof(model));
      _ragProvider = ragProvider ?? throw new ArgumentNullException(nameof(ragProvider));

      Collections = [.. collectionNames.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal)];
      if (Collections.Count == 0)
         throw new ArgumentException("At least one collection name is required.", nameof(collectionNames));
   }

   /// <summary>
   ///    Primary collection. Kept for compatibility; a multi-corpus keeper has no single one, so
   ///    prefer <see cref="Collections" />.
   /// </summary>
   public string CollectionName => Collections[0];

   /// <summary>All collections this keeper searches, in request order.</summary>
   public IReadOnlyList<string> Collections { get; }

   /// <summary>Display name shown in debate logs.</summary>
   public string DisplayName => $"📚 Knowledge Keeper ({_model.ModelName})";

   /// <summary>All interactions recorded during this session.</summary>
   public IReadOnlyList<KnowledgeInteraction> Interactions => _interactions.AsReadOnly();

   /// <summary>
   ///    Performs a semantic search against the knowledge base.
   /// </summary>
   /// <param name="query">Natural language query.</param>
   /// <param name="limit">Maximum number of chunks to retrieve.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Scored search results from the vector store.</returns>
   public async Task<IReadOnlyList<VectorSearchResult>> SearchKnowledgeAsync(
      string query, int limit = 5, CancellationToken ct = default)
   {
      return await SearchAllAsync(query, limit, ct).ConfigureAwait(false);
   }

   /// <summary>
   ///    Searches every collection and merges the hits by score, keeping the best <paramref name="limit" />.
   /// </summary>
   private async Task<IReadOnlyList<VectorSearchResult>> SearchAllAsync(
      string query, int limit, CancellationToken ct)
   {
      if (Collections.Count == 1)
         return await _ragProvider.SearchAsync(Collections[0], query, limit, ct: ct).ConfigureAwait(false);

      // Fetch a wider slice from each corpus, then let the scores decide which survive: asking a
      // corpus for exactly `limit` and merging afterwards would let the largest corpus fill the
      // whole budget before a smaller one contributed anything.
      var perCollection = Math.Max(limit, limit * 2);
      var merged = new List<VectorSearchResult>();

      foreach (var collection in Collections)
      {
         var hits = await _ragProvider.SearchAsync(collection, query, perCollection, ct: ct).ConfigureAwait(false);
         merged.AddRange(hits);
      }

      merged.Sort(static (a, b) => b.Score.CompareTo(a.Score));
      return merged.Count <= limit ? merged : merged.GetRange(0, limit);
   }

   /// <summary>
   ///    Generates an answer to a question using RAG context.
   ///    Searches for relevant chunks, injects them into the prompt, and asks the model.
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
      var context = await GetMergedContextAsync(question, limit, ct).ConfigureAwait(false);

      const string systemPrompt = """
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
         sourceChunks = CountOccurrences(context, "[Source ");
      }

      // 2. Generate answer via dedicated LLM
      var answer = await _model.AskAsync(systemPrompt, userPrompt, temperature, ct).ConfigureAwait(false);

      // 3. Log the interaction
      _interactions.Add(new KnowledgeInteraction(question, answer, sourceChunks));

      return answer;
   }

   /// <summary>
   ///    Answers a question using pre-fetched search results (useful when the caller
   ///    has already performed a search and wants to reuse the results).
   /// </summary>
   public async Task<string> AnswerWithContextAsync(
      string question,
      IReadOnlyList<VectorSearchResult> searchResults,
      float temperature = 0.3f,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(searchResults);

      var sb = new StringBuilder();
      for (var i = 0; i < searchResults.Count; i++)
      {
         sb.AppendLine($"[Source {i + 1} — score: {searchResults[i].Score:F3}]");
         sb.AppendLine(searchResults[i].Text);
         sb.AppendLine();
      }

      const string systemPrompt = """
                                  You are the Knowledge Keeper — a librarian and fact-checker for an AI council debate.
                                  Provide accurate answers based ONLY on the context provided. Cite source numbers.
                                  """;

      var userPrompt = $"""
                        ### Retrieved Context:
                        {sb}

                        ### Question:
                        {question}
                        """;

      var answer = await _model.AskAsync(systemPrompt, userPrompt, temperature, ct).ConfigureAwait(false);
      _interactions.Add(new KnowledgeInteraction(question, answer, searchResults.Count));
      return answer;
   }

   /// <summary>
   ///    Provides structured context for a specific debate round.
   ///    Queries the knowledge base with the round topic and previous round summaries,
   ///    returning a formatted response with sources.
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

      // Search for relevant chunks. One shared empty dictionary per call rather than one per
      // result — and a plain loop instead of Select/ToList, which allocated an iterator and
      // an intermediate list on every round.
      var searchResults = await SearchAllAsync(query, limit, ct).ConfigureAwait(false);
      Dictionary<string, string>? emptyMetadata = null;
      var sources = new List<KnowledgeSource>(searchResults.Count);
      foreach (var r in searchResults)
      {
         if (r.Metadata is null && emptyMetadata is null)
            emptyMetadata = new Dictionary<string, string>();

         sources.Add(new KnowledgeSource(r.Text, r.Score, r.Metadata ?? emptyMetadata!));
      }

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

      // Render the hits already retrieved above. Calling GetContextAsync here would search
      // the same collection with the same query and the same limit a second time, so every
      // round paid for a duplicate embedding call and a duplicate vector-store walk.
      var contextText = RagContextFormatter.Format(searchResults);
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

         answer = await _model.AskAsync(systemPrompt, userPrompt, temperature, ct).ConfigureAwait(false);
      }

      var interaction = new KnowledgeInteraction(query, answer, sources.Count);
      _interactions.Add(interaction);

      return new KnowledgeRoundContext(
         roundNumber,
         answer,
         sources.AsReadOnly(),
         interaction);
   }

   /// <summary>
   ///    Indexes a document into the Knowledge Keeper's collection.
   ///    Convenience wrapper around <see cref="IRagProvider.IndexDocumentAsync" />.
   /// </summary>
   public Task<int> IndexDocumentAsync(
      string documentText,
      Dictionary<string, string>? metadata = null,
      int chunkSize = 500,
      int chunkOverlap = 50,
      CancellationToken ct = default)
   {
      return _ragProvider.IndexDocumentAsync(CollectionName, documentText, metadata, chunkSize, chunkOverlap, ct);
   }

   /// <summary>
   ///    Indexes a file into the Knowledge Keeper's collection.
   /// </summary>
   public Task<int> IndexFileAsync(
      string filePath,
      int chunkSize = 500,
      int chunkOverlap = 50,
      CancellationToken ct = default)
   {
      return _ragProvider.IndexFileAsync(CollectionName, filePath, chunkSize, chunkOverlap, ct);
   }

   /// <summary>
   ///    Searches every collection, merges by score, and renders the result the way
   ///    <see cref="IRagProvider.GetContextAsync" /> would for a single one.
   /// </summary>
   private async Task<string> GetMergedContextAsync(string query, int limit, CancellationToken ct)
   {
      if (Collections.Count == 1)
         return await _ragProvider.GetContextAsync(Collections[0], query, limit, ct).ConfigureAwait(false);

      var hits = await SearchAllAsync(query, limit, ct).ConfigureAwait(false);
      if (hits.Count == 0)
         return string.Empty;

      return RagContextFormatter.Format(hits);
   }

   /// <summary>
   ///    Counts non-overlapping occurrences of a substring without allocating a string array.
   /// </summary>
   private static int CountOccurrences(string text, string pattern)
   {
      if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
         return 0;

      var count = 0;
      var idx = 0;
      while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
      {
         count++;
         idx += pattern.Length;
      }

      return count;
   }
}
