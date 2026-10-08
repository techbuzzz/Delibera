using Microsoft.Extensions.Configuration;

namespace Delibera.Core.Providers.RAG;

/// <summary>
///    Factory for creating <see cref="IRagProvider" /> instances from configuration.
///    Register custom builders to support additional vector databases.
/// </summary>
public sealed class VectorStoreFactory :
   CachingFactory<Func<IConfigurationSection, IEmbeddingProvider, IRagProvider>, IRagProvider>, IVectorStoreFactory
{
   /// <summary>
   ///    Creates a new factory with the built-in Qdrant and PgVector builders registered.
   /// </summary>
   public VectorStoreFactory()
   {
      RegisterBuilder("Qdrant", (config, embeddings) =>
      {
         var host = config["Host"] ?? "localhost";
         var port = int.TryParse(config["Port"], out var p)
            ? p
            : 6334;
         var https = bool.TryParse(config["Https"], out var h) && h;
         var apiKey = config["ApiKey"];

         return new QdrantRagProvider(embeddings, host, port, https, apiKey);
      });

      RegisterBuilder("PgVector", (config, embeddings) =>
      {
         var connectionString = config["ConnectionString"] ??
                                throw new InvalidOperationException(
                                   "PgVector requires a 'ConnectionString' configuration key.");
         return new PgVectorRagProvider(embeddings, connectionString);
      });
   }

   /// <inheritdoc />
   IVectorStoreFactory IVectorStoreFactory.RegisterBuilder(
      string providerType,
      Func<IConfigurationSection, IEmbeddingProvider, IRagProvider> builder)
   {
      RegisterBuilder(providerType, builder);
      return this;
   }

   /// <summary>
   ///    Creates (or returns cached) a RAG provider instance.
   /// </summary>
   public IRagProvider Create(string name, string providerType, IConfigurationSection config,
      IEmbeddingProvider embeddingProvider)
   {
      return GetOrCreate(name, providerType, b => b(config, embeddingProvider));
   }

   /// <summary>Returns a cached provider by name.</summary>
   public IRagProvider? GetProvider(string name)
   {
      return GetInstance(name);
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      foreach (var p in EnumerateInstances())
         await p.DisposeAsync().ConfigureAwait(false);
      ClearInstances();
   }

   /// <summary>
   ///    Creates a Qdrant RAG provider with direct parameters.
   /// </summary>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="host">Qdrant host name.</param>
   /// <param name="port">Qdrant gRPC port.</param>
   /// <param name="https">Whether to connect over TLS.</param>
   /// <param name="apiKey">Optional Qdrant API key.</param>
   /// <param name="logger">
   ///    Optional logger. Supplying one lets the provider report a collection that still holds
   ///    legacy random-id points from Delibera 10.5.0 or earlier.
   /// </param>
   public IRagProvider CreateQdrant(
      IEmbeddingProvider embeddingProvider,
      string host = "localhost",
      int port = 6334,
      bool https = false,
      string? apiKey = null,
      ILogger? logger = null)
   {
      var key = $"qdrant:{host}:{port}";
      if (GetInstance(key) is { } existing) return existing;

      var provider = new QdrantRagProvider(embeddingProvider, host, port, https, apiKey, logger);
      return CacheInstance(key, provider);
   }

   /// <summary>
   ///    Creates a PgVector RAG provider with a connection string.
   /// </summary>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="connectionString">PostgreSQL connection string.</param>
   /// <param name="logger">
   ///    Optional logger. Supplying one lets the provider report a collection that still holds
   ///    legacy random-id points from Delibera 10.5.0 or earlier.
   /// </param>
   public IRagProvider CreatePgVector(IEmbeddingProvider embeddingProvider, string connectionString,
      ILogger? logger = null)
   {
      var key = $"pgvector:{connectionString.GetHashCode():X8}";
      if (GetInstance(key) is { } existing) return existing;

      var provider = new PgVectorRagProvider(embeddingProvider, connectionString, logger);
      return CacheInstance(key, provider);
   }

   /// <inheritdoc />
   protected override void DisposeInstances()
   {
      foreach (var p in EnumerateInstances())
         p.DisposeAsync().AsTask().GetAwaiter().GetResult();
   }
}
