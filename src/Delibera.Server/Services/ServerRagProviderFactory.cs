using Delibera.Core;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers.LLM;

namespace Delibera.Server.Services;

/// <summary>
///    Builds the <see cref="IRagProvider" /> the server uses for corpus indexing and for the
///    Knowledge Keeper inside a debate.
/// </summary>
/// <remarks>
///    <para>
///       Nothing registered this before. <c>AddDelibera</c> provides
///       <see cref="ILLMProviderFactory" /> and <see cref="IVectorStoreFactory" />, but no
///       <see cref="IRagProvider" /> was ever resolved from DI, so <c>Rag.Enabled = true</c> had no
///       effect on any debate: the templates built no keeper, and the corpus API computed a chunk
///       count without embedding or storing anything.
///    </para>
///    <para>
///       One instance is shared by indexing and retrieval on purpose. They must agree on the
///       embedding model and vector size — a corpus embedded with one model and searched with
///       another produces silently meaningless similarity scores.
///    </para>
/// </remarks>
public sealed class ServerRagProviderFactory(
   IVectorStoreFactory vectorStores,
   IConfiguration configuration,
   ILoggerFactory loggerFactory) : IDisposable
{
   private readonly Lock _gate = new();
   private OllamaProvider? _embeddingSource;
   private IRagProvider? _rag;

    /// <summary>Whether <c>Delibera:Rag:Enabled</c> is set.</summary>
    public bool Enabled => configuration.GetValue(BuiltIn.ConfigKeys.RagEnabled, false);
 
    /// <summary>Provider type, e.g. <c>Qdrant</c> or <c>PgVector</c>.</summary>
    public string ProviderType => configuration[BuiltIn.ConfigKeys.RagProviderType] ?? "Qdrant";

   public void Dispose()
   {
      _rag = null;
      _embeddingSource?.Dispose();
      _embeddingSource = null;
   }

   /// <summary>Collection name for a corpus with the given id.</summary>
   /// <remarks>
   ///    One collection per corpus rather than one shared collection: <see cref="IRagProvider" />
   ///    search takes a single collection and offers no filter argument, so a single collection
   ///    could not keep two corpora from leaking into each other's retrieval.
   /// </remarks>
   public static string CollectionForCorpus(string corpusId)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(corpusId);
      return $"corpus_{corpusId}";
   }

   /// <summary>
   ///    Returns the shared provider, or <c>null</c> when RAG is disabled. Cached so that the
   ///    embedding client — and therefore the embedding model — stays stable for the process.
   /// </summary>
   public IRagProvider? Get()
   {
      if (!Enabled)
         return null;

      lock (_gate)
      {
         if (_rag is not null)
            return _rag;

          var endpoint = configuration[BuiltIn.ConfigKeys.ProvidersDefaultEndpoint]
                         ?? throw new InvalidOperationException(
                            "Delibera:Providers:DefaultEndpoint is required for RAG.");
          var apiKey = configuration[BuiltIn.ConfigKeys.ProvidersApiKey];
          var embeddingModel = configuration[BuiltIn.ConfigKeys.ProvidersEmbeddingModel] ?? BuiltIn.Models.DefaultEmbedding;
 
          // The embedding provider borrows its HTTP client from the Ollama provider, so the two
          // share one endpoint. That is why RAG needs a provider that serves embeddings: Ollama
          // Cloud exposes none, so this configuration only works against a local Ollama.
          _embeddingSource = new OllamaProvider(endpoint, apiKey ?? string.Empty, TimeSpan.FromMinutes(2));
          var embeddings = new OllamaEmbeddingProvider(_embeddingSource, embeddingModel);
 
          // The factory reads Host/Port (Qdrant) or ConnectionString (PgVector) from the section,
          // which is exactly the shape of Delibera:Rag in configuration.
          _rag = vectorStores.Create(embeddingModel, ProviderType, configuration.GetSection("Delibera:Rag"), embeddings);

         loggerFactory.CreateLogger<ServerRagProviderFactory>()
            .LogInformation(
               "RAG provider ready: {ProviderType}, embeddings '{EmbeddingModel}' at {Endpoint}.",
               ProviderType, embeddingModel, endpoint);

         return _rag;
      }
   }
}
