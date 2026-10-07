namespace Delibera.Core.Providers.RAG;

/// <summary>
///    RAG provider backed by PostgreSQL + pgvector.
///    Combines an <see cref="IEmbeddingProvider" /> with a <see cref="PgVectorStore" />
///    to index documents and perform semantic search.
/// </summary>
/// <remarks>
///    <para>
///       Functionally equivalent to <see cref="QdrantRagProvider" /> but uses
///       PostgreSQL as the vector store — ideal when you already have a Postgres database
///       and want to avoid running a separate vector-DB service.
///    </para>
/// </remarks>
public sealed class PgVectorRagProvider : BaseRagProvider
{
   /// <summary>
   ///    Creates a PgVector RAG provider from an existing vector store and embedding provider.
   /// </summary>
   /// <param name="vectorStore">PgVector vector store instance.</param>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="logger">Optional logger used to report collections holding legacy random-id points.</param>
   public PgVectorRagProvider(IVectorStore vectorStore, IEmbeddingProvider embeddingProvider, ILogger? logger = null)
      : base(vectorStore, embeddingProvider, logger)
   {
   }

   /// <summary>
   ///    Convenience constructor that creates a <see cref="PgVectorStore" /> internally.
   /// </summary>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="connectionString">PostgreSQL connection string.</param>
   /// <param name="logger">Optional logger used to report collections holding legacy random-id points.</param>
   public PgVectorRagProvider(
      IEmbeddingProvider embeddingProvider,
      string connectionString,
      ILogger? logger = null)
      : this(new PgVectorStore(connectionString), embeddingProvider, logger)
   {
   }

   /// <inheritdoc />
   public override string ProviderName => "PgVectorRAG";
}
