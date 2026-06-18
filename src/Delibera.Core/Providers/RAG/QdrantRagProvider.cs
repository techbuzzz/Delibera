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
   public QdrantRagProvider(IVectorStore vectorStore, IEmbeddingProvider embeddingProvider)
      : base(vectorStore, embeddingProvider)
   {
   }

   /// <summary>
   ///    Convenience constructor that creates a <see cref="QdrantVectorStore" /> internally.
   /// </summary>
   public QdrantRagProvider(
      IEmbeddingProvider embeddingProvider,
      string qdrantHost = "localhost",
      int qdrantPort = 6334,
      bool https = false,
      string? apiKey = null)
      : this(new QdrantVectorStore(qdrantHost, qdrantPort, https, apiKey), embeddingProvider)
   {
   }

   /// <inheritdoc />
   public override string ProviderName => "QdrantRAG";
}