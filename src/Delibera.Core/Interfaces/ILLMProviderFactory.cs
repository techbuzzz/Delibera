using Microsoft.Extensions.Configuration;

#pragma warning disable IDE1006 // 'LLM' acronym kept all-caps by convention; renaming is a breaking API change

namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for creating and managing <see cref="ILLMProvider" /> instances.
///    Enables dependency injection and testability of provider creation.
/// </summary>
public interface ILLMProviderFactory : IDisposable
{
   /// <summary>
   ///    Returns the set of registered provider type names.
   /// </summary>
   IReadOnlyCollection<string> RegisteredTypes { get; }

   /// <summary>
   ///    Registers a builder function for a provider type (e.g., "Ollama", "OpenAI").
   /// </summary>
   /// <param name="providerType">Provider type key (case-insensitive).</param>
   /// <param name="builder">Factory function that takes a configuration section and returns a provider.</param>
   /// <returns>This factory for fluent chaining.</returns>
   ILLMProviderFactory RegisterBuilder(string providerType, Func<IConfigurationSection, ILLMProvider> builder);

   /// <summary>
   ///    Creates or returns a cached <see cref="ILLMProvider" /> from configuration.
   /// </summary>
   /// <param name="name">Unique instance name used for caching.</param>
   /// <param name="providerType">Registered provider type key.</param>
   /// <param name="config">Configuration section with provider-specific settings.</param>
   /// <returns>The created or cached provider instance.</returns>
   ILLMProvider Create(string name, string providerType, IConfigurationSection config);

   /// <summary>
   ///    Returns a previously created provider by name, or <c>null</c> if not found.
   /// </summary>
   /// <param name="name">Instance name.</param>
   ILLMProvider? GetProvider(string name);

   /// <summary>
   ///    Returns all currently cached provider instances.
   /// </summary>
   IReadOnlyDictionary<string, ILLMProvider> GetAllProviders();
}