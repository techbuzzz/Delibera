using Microsoft.Extensions.Configuration;

namespace Delibera.Core.Providers.RAG;

/// <summary>
/// Factory for creating <see cref="IRagProvider"/> instances from configuration.
/// Register custom builders to support additional vector databases.
/// </summary>
public sealed class RagProviderFactory : IRagProviderFactory, IAsyncDisposable
{
   private readonly Dictionary<string, Func<IConfigurationSection, IEmbeddingProvider, IRagProvider>> _builders = new(StringComparer.OrdinalIgnoreCase);
   private readonly Dictionary<string, IRagProvider> _instances = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>
   /// Creates a new factory with the built-in Qdrant and PgVector builders registered.
   /// </summary>
   public RagProviderFactory()
   {
      RegisterBuilder("Qdrant", (config, embeddings) =>
      {
         var host = config["Host"] ?? "localhost";
         var port = int.TryParse(config["Port"], out var p) ? p : 6334;
         var https = bool.TryParse(config["Https"], out var h) && h;
         var apiKey = config["ApiKey"];

         return new QdrantRagProvider(embeddings, host, port, https, apiKey);
      });

      RegisterBuilder("PgVector", (config, embeddings) =>
      {
         var connectionString = config["ConnectionString"]
               ?? throw new InvalidOperationException("PgVector requires a 'ConnectionString' configuration key.");
         return new PgVectorRagProvider(embeddings, connectionString);
      });
   }

   /// <summary>
   /// Registers a custom RAG provider builder (e.g., for pgvector, Pinecone, Weaviate).
   /// </summary>
   /// <param name="providerType">Provider type key (e.g., "Qdrant", "PgVector").</param>
   /// <param name="builder">Factory function that receives config + embeddings and returns a provider.</param>
   public RagProviderFactory RegisterBuilder(
       string providerType,
       Func<IConfigurationSection, IEmbeddingProvider, IRagProvider> builder)
   {
      _builders[providerType] = builder ?? throw new ArgumentNullException(nameof(builder));
      return this;
   }

   /// <inheritdoc/>
   IRagProviderFactory IRagProviderFactory.RegisterBuilder(
       string providerType,
       Func<IConfigurationSection, IEmbeddingProvider, IRagProvider> builder)
       => RegisterBuilder(providerType, builder);

   /// <summary>
   /// Creates (or returns cached) a RAG provider instance.
   /// </summary>
   /// <param name="name">Unique instance name for caching.</param>
   /// <param name="providerType">Provider type (must be registered).</param>
   /// <param name="config">Configuration section with provider-specific settings.</param>
   /// <param name="embeddingProvider">Embedding provider to use for vectorisation.</param>
   public IRagProvider Create(string name, string providerType, IConfigurationSection config, IEmbeddingProvider embeddingProvider)
   {
      if (_instances.TryGetValue(name, out var existing))
         return existing;

      if (!_builders.TryGetValue(providerType, out var builder))
         throw new InvalidOperationException(
             $"Unknown RAG provider type '{providerType}'. Registered: {string.Join(", ", _builders.Keys)}");

      var provider = builder(config, embeddingProvider);
      _instances[name] = provider;
      return provider;
   }

   /// <summary>
   /// Creates a Qdrant RAG provider with direct parameters.
   /// </summary>
   public IRagProvider CreateQdrant(
       IEmbeddingProvider embeddingProvider,
       string host = "localhost",
       int port = 6334,
       bool https = false,
       string? apiKey = null)
   {
      var key = $"qdrant:{host}:{port}";
      if (_instances.TryGetValue(key, out var existing))
         return existing;

      var provider = new QdrantRagProvider(embeddingProvider, host, port, https, apiKey);
      _instances[key] = provider;
      return provider;
   }

   /// <summary>
   /// Creates a PgVector RAG provider with a connection string.
   /// </summary>
   /// <param name="embeddingProvider">Embedding provider for vectorisation.</param>
   /// <param name="connectionString">PostgreSQL connection string.</param>
   public IRagProvider CreatePgVector(IEmbeddingProvider embeddingProvider, string connectionString)
   {
      var key = $"pgvector:{connectionString.GetHashCode():X8}";
      if (_instances.TryGetValue(key, out var existing))
         return existing;

      var provider = new PgVectorRagProvider(embeddingProvider, connectionString);
      _instances[key] = provider;
      return provider;
   }

   /// <summary>Returns a cached provider by name.</summary>
   public IRagProvider? GetProvider(string name) =>
       _instances.TryGetValue(name, out var p) ? p : null;

   /// <summary>All registered provider type names.</summary>
   public IReadOnlyCollection<string> RegisteredTypes => _builders.Keys.ToList().AsReadOnly();

   /// <inheritdoc/>
   public async ValueTask DisposeAsync()
   {
      foreach (var p in _instances.Values)
         await p.DisposeAsync();
      _instances.Clear();
   }
}
