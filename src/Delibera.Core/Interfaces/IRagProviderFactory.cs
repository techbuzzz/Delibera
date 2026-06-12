using Microsoft.Extensions.Configuration;

namespace Delibera.Core.Interfaces;

/// <summary>
/// Abstraction for creating and managing <see cref="IRagProvider"/> instances.
/// Supports registration of custom vector database builders (Qdrant, pgvector, etc.).
/// </summary>
public interface IRagProviderFactory : IAsyncDisposable
{
   /// <summary>
   /// Registers a custom builder for a RAG provider type.
   /// </summary>
   /// <param name="providerType">Provider type key (case-insensitive, e.g., "Qdrant", "PgVector").</param>
   /// <param name="builder">Factory function that takes a config section and embedding provider.</param>
   /// <returns>This factory for fluent chaining.</returns>
   IRagProviderFactory RegisterBuilder(
       string providerType,
       Func<IConfigurationSection, IEmbeddingProvider, IRagProvider> builder);

   /// <summary>
   /// Creates or returns a cached <see cref="IRagProvider"/>.
   /// </summary>
   /// <param name="name">Unique instance name for caching.</param>
   /// <param name="providerType">Registered provider type key.</param>
   /// <param name="config">Configuration section with provider-specific settings.</param>
   /// <param name="embeddingProvider">Embedding provider for vectorization.</param>
   /// <returns>The created or cached RAG provider.</returns>
   IRagProvider Create(string name, string providerType, IConfigurationSection config, IEmbeddingProvider embeddingProvider);

   /// <summary>
   /// Returns a previously created provider by name, or <c>null</c> if not found.
   /// </summary>
   /// <param name="name">Instance name.</param>
   IRagProvider? GetProvider(string name);

   /// <summary>
   /// Returns the set of registered provider type names.
   /// </summary>
   IReadOnlyCollection<string> RegisteredTypes { get; }
}
