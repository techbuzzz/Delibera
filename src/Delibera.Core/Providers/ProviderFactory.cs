using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.Configuration;

namespace Delibera.Core.Providers;

/// <summary>
///    Generic registry-and-cache factory shared by <see cref="ProviderFactory" /> and
///    <see cref="RAG.RagProviderFactory" />. Stores builders keyed by name and the instances
///    they have produced so subsequent <c>Create</c> calls return the same object.
/// </summary>
public abstract class CachingFactory<TBuilder, TInstance>
   : IDisposable
   where TInstance : class
{
   private readonly Dictionary<string, TBuilder> _builders = new(StringComparer.OrdinalIgnoreCase);
   private readonly Dictionary<string, TInstance> _instances = new(StringComparer.OrdinalIgnoreCase);
   private bool _disposed;

   /// <summary>Registered provider type names (read-only view).</summary>
   public IReadOnlyCollection<string> RegisteredTypes => _builders.Keys.ToList().AsReadOnly();

   /// <summary>All currently cached provider instances.</summary>
   public IReadOnlyDictionary<string, TInstance> GetAllInstances() => _instances;

   /// <summary>Returns a cached instance by name, or <c>null</c>.</summary>
   public TInstance? GetInstance(string name) => _instances.TryGetValue(name, out var p) ? p : null;

   /// <summary>Registers a builder for a new provider type (e.g., "OpenAI", "YandexGPT").</summary>
   public CachingFactory<TBuilder, TInstance> RegisterBuilder(string providerType, TBuilder builder)
   {
      ArgumentNullException.ThrowIfNull(builder);
      _builders[providerType] = builder;
      return this;
   }

   /// <summary>
   ///    Returns a cached instance, or builds and caches a new one. Throws if no builder
   ///    is registered for <paramref name="providerType" />.
   /// </summary>
   protected TInstance GetOrCreate(string name, string providerType, Func<TBuilder, TInstance> build)
   {
      if (_instances.TryGetValue(name, out var existing)) return existing;
      if (!_builders.TryGetValue(providerType, out var builder))
         throw new InvalidOperationException(
            $"Unknown provider type '{providerType}'. Registered: {string.Join(", ", _builders.Keys)}");

      var instance = build(builder);
      _instances[name] = instance;
      return instance;
   }

   /// <summary>
   ///    Stores a fully-constructed instance in the cache. Used by derived factories
   ///    that create providers with direct parameters rather than through a builder.
   /// </summary>
   protected TInstance CacheInstance(string name, TInstance instance)
   {
      _instances[name] = instance;
      return instance;
   }

   /// <summary>Iterates every cached instance — used by the derived <c>Dispose</c> methods.</summary>
   protected IEnumerable<TInstance> EnumerateInstances() => _instances.Values;

   /// <summary>Clears the cache without disposing anything.</summary>
   protected void ClearInstances() => _instances.Clear();

   /// <inheritdoc />
   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      DisposeInstances();
      ClearInstances();
      GC.SuppressFinalize(this);
   }

   /// <summary>Disposes every cached instance. Subclasses override to call the correct dispose method.</summary>
   protected abstract void DisposeInstances();
}

/// <summary>
///    Factory for creating <see cref="ILLMProvider" /> instances.
///    Supports registration of custom provider builders for extensibility.
/// </summary>
public sealed class ProviderFactory : CachingFactory<Func<IConfigurationSection, ILLMProvider>, ILLMProvider>, ILLMProviderFactory
{
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
      RegisterBuilder(providerType, builder);
      return this;
   }

   /// <summary>
   ///    Creates (or returns a cached) provider from configuration.
   /// </summary>
   public ILLMProvider Create(string name, string providerType, IConfigurationSection config) =>
      GetOrCreate(name, providerType, b => b(config));

   /// <summary>Returns a cached provider by name, or <c>null</c>.</summary>
   public ILLMProvider? GetProvider(string name) => GetInstance(name);

   /// <summary>All created provider instances.</summary>
   public IReadOnlyDictionary<string, ILLMProvider> GetAllProviders() => GetAllInstances();

   /// <summary>Creates an Ollama provider with direct parameters.</summary>
   public OllamaProvider CreateOllama(string endpoint, string apiKey = "")
   {
      var key = $"ollama:{endpoint}";
      if (GetInstance(key) is OllamaProvider existing) return existing;

      var provider = new OllamaProvider(endpoint, apiKey);
      CacheInstance(key, provider);
      return provider;
   }

   /// <inheritdoc />
   protected override void DisposeInstances()
   {
      foreach (var p in EnumerateInstances()) p.Dispose();
   }
}
