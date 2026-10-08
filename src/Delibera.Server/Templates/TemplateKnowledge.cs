using Delibera.Core.Interfaces;
using Delibera.Core.Providers;

namespace Delibera.Server.Templates;

/// <summary>
///    Wires a template's Knowledge Keeper.
/// </summary>
/// <remarks>
///    <para>
///       Every template has declared <c>RagEnabled =&gt; true</c> since 10.3.0, but nothing read
///       that flag and no template ever called <c>WithKnowledgeKeeper</c>. A debate therefore ran
///       with no retrieval at all, and <c>DebateRound.KnowledgeInteractions</c> was always empty —
///       regardless of the corpora a caller had indexed.
///    </para>
///    <para>
///       Grounding is attached only when RAG is enabled <i>and</i> the request names corpora: with
///       nothing indexed, a keeper would answer every round from an empty collection and inject
///       "no relevant documents found" noise into the transcript at the cost of an embedding call
///       per round.
///    </para>
/// </remarks>
internal static class TemplateKnowledge
{
   public static ICouncilBuilder Attach(
      ICouncilBuilder builder,
      CreateDebateRequest request,
      IServiceProvider services)
   {
      ArgumentNullException.ThrowIfNull(builder);
      ArgumentNullException.ThrowIfNull(request);
      ArgumentNullException.ThrowIfNull(services);

      var corpusIds = request.CorpusIds?.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
      if (corpusIds is not { Length: > 0 })
         return builder;

      if (services.GetService<ServerRagProviderFactory>() is not { } ragFactory || !ragFactory.Enabled)
         return builder;

      var rag = ragFactory.Get();
      if (rag is null)
         return builder;

      // The keeper's model is a council member, so it uses the configured strong model: retrieval
      // synthesis is a comprehension job, and answering it on the fast model showed up as short,
      // unsourced answers.
      var configuration = services.GetService<IConfiguration>();
      var strongModel = configuration?["Delibera:Models:Strong"] ?? "qwen2.5:7b";
      var endpoint = configuration?["Delibera:Providers:DefaultEndpoint"] ?? "http://localhost:11434";
      var apiKey = configuration?["Delibera:Providers:ApiKey"];

      // A dedicated factory instance rather than one from DI: disposing it here must not tear
      // down the provider the debate itself is about to use.
      using var providerFactory = new ProviderFactory();
      var llm = string.IsNullOrEmpty(apiKey)
         ? providerFactory.CreateOllama(endpoint)
         : providerFactory.CreateCloudOllama(endpoint, apiKey);

      var collections = corpusIds.Select(ServerRagProviderFactory.CollectionForCorpus).ToArray();
      var keeper = new KnowledgeKeeper(rag, new CouncilMember(strongModel, llm, "Knowledge Keeper"), collections);

      return builder.WithKnowledgeKeeper(keeper);
   }
}
