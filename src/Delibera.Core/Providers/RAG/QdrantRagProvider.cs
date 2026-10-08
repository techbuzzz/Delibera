namespace Delibera.Core.Providers.RAG;

/// <summary>
///    RAG provider backed by Qdrant vector database.
///    Combines an <see cref="IEmbeddingProvider" /> with a <see cref="QdrantVectorStore" />
///    to index documents and perform semantic search.
/// </summary>
public sealed class QdrantRagProvider : BaseRagProvider
{
   /// <summary>
   ///    Creates a Qdrant RAG provider.
   /// </summary>
   /// <param name="vectorStore">Qdrant vector store instance.</param>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="logger">Optional logger used to report collections holding legacy random-id points.</param>
   public QdrantRagProvider(IVectorStore vectorStore, IEmbeddingProvider embeddingProvider, ILogger? logger = null)
      : base(vectorStore, embeddingProvider, logger)
   {
   }

   /// <summary>
   ///    Convenience constructor that creates a <see cref="QdrantVectorStore" /> internally.
   /// </summary>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="qdrantHost">Qdrant host name.</param>
   /// <param name="qdrantPort">Qdrant gRPC port.</param>
   /// <param name="https">Whether to use TLS.</param>
   /// <param name="apiKey">Optional API key.</param>
   /// <param name="logger">Optional logger used to report collections holding legacy random-id points.</param>
   public QdrantRagProvider(
      IEmbeddingProvider embeddingProvider,
      string qdrantHost = "localhost",
      int qdrantPort = 6334,
      bool https = false,
      string? apiKey = null,
      ILogger? logger = null)
      : this(new QdrantVectorStore(qdrantHost, qdrantPort, https, apiKey), embeddingProvider, logger)
   {
   }

   /// <inheritdoc />
   public override string ProviderName => "QdrantRAG";
}
