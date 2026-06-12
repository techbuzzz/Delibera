using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.Configuration;

namespace Delibera.Core.Providers;

/// <summary>
///    Factory for creating <see cref="ILLMProvider" /> instances.
///    Supports registration of custom provider builders for extensibility.
/// </summary>
public sealed class ProviderFactory : ILLMProviderFactory, IDisposable
{
   private readonly Dictionary<string, Func<IConfigurationSection, ILLMProvider>> _builders = new(StringComparer.OrdinalIgnoreCase);
   private readonly Dictionary<string, ILLMProvider> _instances = new(StringComparer.OrdinalIgnoreCase);
   private bool _disposed;

   /// <summary>Creates a factory with the built-in Ollama provider registered.</summary>
   public ProviderFactory()
   {
      RegisterBuilder("Ollama", config =>
      {
         var endpoint = config["Endpoint"] ?? "http://localhost:11434";
         var apiKey = config["ApiKey"] ?? "";
         return new OllamaProvider(endpoint, apiKey);
      });
   }

   /// <inheritdoc />
   ILLMProviderFactory ILLMProviderFactory.RegisterBuilder(string providerType, Func<IConfigurationSection, ILLMProvider> builder)
   {
      return RegisterBuilder(providerType, builder);
   }

   /// <summary>
   ///    Creates (or returns a cached) provider from configuration.
   /// </summary>
   public ILLMProvider Create(string name, string providerType, IConfigurationSection config)
   {
      if (_instances.TryGetValue(name, out var existing)) return existing;
      if (!_builders.TryGetValue(providerType, out var builder))
         throw new InvalidOperationException(
            $"Unknown provider type '{providerType}'. Registered: {string.Join(", ", _builders.Keys)}");

      var provider = builder(config);
      _instances[name] = provider;
      return provider;
   }

   /// <summary>Returns a cached provider by name, or <c>null</c>.</summary>
   public ILLMProvider? GetProvider(string name)
   {
      return _instances.TryGetValue(name, out var p)
         ? p
         : null;
   }

   /// <summary>All created provider instances.</summary>
   public IReadOnlyDictionary<string, ILLMProvider> GetAllProviders()
   {
      return _instances;
   }

   /// <summary>Registered provider type names.</summary>
   public IReadOnlyCollection<string> RegisteredTypes => _builders.Keys.ToList().AsReadOnly();

   /// <inheritdoc />
   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      foreach (var p in _instances.Values) p.Dispose();
      _instances.Clear();
      GC.SuppressFinalize(this);
   }

   /// <summary>
   ///    Registers a builder for a new provider type (e.g., "OpenAI", "YandexGPT").
   /// </summary>
   public ProviderFactory RegisterBuilder(string providerType, Func<IConfigurationSection, ILLMProvider> builder)
   {
      _builders[providerType] = builder ?? throw new ArgumentNullException(nameof(builder));
      return this;
   }

   /// <summary>Creates an Ollama provider with direct parameters.</summary>
   public OllamaProvider CreateOllama(string endpoint, string apiKey = "")
   {
      var key = $"ollama:{endpoint}";
      if (_instances.TryGetValue(key, out var existing) && existing is OllamaProvider op)
         return op;

      var provider = new OllamaProvider(endpoint, apiKey);
      _instances[key] = provider;
      return provider;
   }
}