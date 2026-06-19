using Microsoft.Extensions.Configuration;

namespace Delibera.Core.Providers.RAG;

/// <summary>
///    Factory for creating <see cref="IRagProvider" /> instances from configuration.
///    Register custom builders to support additional vector databases.
/// </summary>
public sealed class RagProviderFactory : CachingFactory<Func<IConfigurationSection, IEmbeddingProvider, IRagProvider>, IRagProvider>, IRagProviderFactory, IAsyncDisposable
{
   /// <summary>
   ///    Creates a new factory with the built-in Qdrant and PgVector builders registered.
   /// </summary>
   public RagProviderFactory()
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
         var connectionString = config["ConnectionString"] ?? throw new InvalidOperationException("PgVector requires a 'ConnectionString' configuration key.");
         return new PgVectorRagProvider(embeddings, connectionString);
      });
   }

   /// <inheritdoc />
   IRagProviderFactory IRagProviderFactory.RegisterBuilder(
      string providerType,
      Func<IConfigurationSection, IEmbeddingProvider, IRagProvider> builder)
   {
      RegisterBuilder(providerType, builder);
      return this;
   }

   /// <summary>
   ///    Creates (or returns cached) a RAG provider instance.
   /// </summary>
   public IRagProvider Create(string name, string providerType, IConfigurationSection config, IEmbeddingProvider embeddingProvider)
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
         await p.DisposeAsync();
      ClearInstances();
   }

   /// <summary>
   ///    Creates a Qdrant RAG provider with direct parameters.
   /// </summary>
   public IRagProvider CreateQdrant(
      IEmbeddingProvider embeddingProvider,
      string host = "localhost",
      int port = 6334,
      bool https = false,
      string? apiKey = null)
   {
      var key = $"qdrant:{host}:{port}";
      if (GetInstance(key) is { } existing) return existing;

      var provider = new QdrantRagProvider(embeddingProvider, host, port, https, apiKey);
      return CacheInstance(key, provider);
   }

   /// <summary>
   ///    Creates a PgVector RAG provider with a connection string.
   /// </summary>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="connectionString">PostgreSQL connection string.</param>
   public IRagProvider CreatePgVector(IEmbeddingProvider embeddingProvider, string connectionString)
   {
      var key = $"pgvector:{connectionString.GetHashCode():X8}";
      if (GetInstance(key) is { } existing) return existing;

      var provider = new PgVectorRagProvider(embeddingProvider, connectionString);
      return CacheInstance(key, provider);
   }

   /// <inheritdoc />
   protected override void DisposeInstances()
   {
      foreach (var p in EnumerateInstances())
         p.DisposeAsync().AsTask().GetAwaiter().GetResult();
   }
}