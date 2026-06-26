using Delibera.Core.Compression;
using Delibera.Core.Council;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Delibera.Core.DependencyInjection;

/// <summary>
///    Extension methods for registering Delibera services with <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
   /// <summary>
   ///    Registers core Delibera services with default options.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <returns>The service collection for chaining.</returns>
   /// <remarks>
   ///    Registers:
   ///    <list type="bullet">
   ///       <item><see cref="ILLMProviderFactory" /> → <see cref="ProviderFactory" /> (singleton)</item>
   ///       <item><see cref="IRagProviderFactory" /> → <see cref="RagProviderFactory" /> (singleton)</item>
   ///       <item><see cref="ICompressionFactory" /> → <see cref="CompressionService" /> (singleton)</item>
   ///       <item><see cref="ICouncilBuilder" /> → <see cref="CouncilBuilder" /> (transient)</item>
   ///    </list>
   /// </remarks>
   public static IServiceCollection AddDelibera(this IServiceCollection services)
   {
      services.TryAddSingleton<ILLMProviderFactory, ProviderFactory>();
      services.TryAddSingleton<IRagProviderFactory, RagProviderFactory>();
      services.TryAddSingleton<ICompressionFactory, CompressionService>();
      services.TryAddTransient<ICouncilBuilder, CouncilBuilder>();

      return services;
   }

   /// <summary>
   ///    Registers core Delibera services and binds <see cref="CouncilOptions" /> from configuration.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <param name="configuration">Configuration root or section containing council settings.</param>
   /// <param name="sectionName">Configuration section name (default: "Delibera").</param>
   /// <returns>The service collection for chaining.</returns>
   public static IServiceCollection AddDelibera(
      this IServiceCollection services,
      IConfiguration configuration,
      string sectionName = CouncilOptions.SectionName)
   {
      services.AddDelibera();

      var section = configuration.GetSection(sectionName);
      if (section.Exists()) services.Configure<CouncilOptions>(section);

      return services;
   }

   /// <summary>
   ///    Registers core Delibera services with a custom options configuration delegate.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <param name="configureOptions">Delegate to configure <see cref="CouncilOptions" />.</param>
   /// <returns>The service collection for chaining.</returns>
   public static IServiceCollection AddDelibera(
      this IServiceCollection services,
      Action<CouncilOptions> configureOptions)
   {
      ArgumentNullException.ThrowIfNull(configureOptions);
      services.AddDelibera();
      services.Configure(configureOptions);

      return services;
   }

   /// <summary>
   ///    Registers core Delibera services and wires the framework into the host's
   ///    <see cref="ILoggerFactory" />. A <see cref="CouncilBuilder" /> resolved from the
   ///    container is automatically decorated with a logger, so any debate started via DI
   ///    logs to the host's pipeline (console, file, OpenTelemetry, …) in addition to the
   ///    in-memory <see cref="ExecutionLog" /> collection.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <param name="configuration">Configuration root or section.</param>
   /// <param name="loggerFactory">Host logger factory.</param>
   /// <param name="sectionName">Configuration section name (default: "Delibera").</param>
   /// <returns>The service collection for chaining.</returns>
   public static IServiceCollection AddDelibera(
      this IServiceCollection services,
      IConfiguration configuration,
      ILoggerFactory loggerFactory,
      string sectionName = CouncilOptions.SectionName)
   {
      ArgumentNullException.ThrowIfNull(loggerFactory);
      services.AddDelibera(configuration, sectionName);
      services.TryAddSingleton(loggerFactory);

      // Replace the transient builder registration so every resolved ICouncilBuilder
      // gets a logger injected automatically. Consumers who build the executor themselves
      // can still call WithLogger(...) explicitly to override.
      services.Replace(ServiceDescriptor.Transient<ICouncilBuilder>(sp =>
      {
         var builder = new CouncilBuilder();
         var lf = sp.GetService<ILoggerFactory>();
         if (lf is not null)
            builder.WithLogger(lf.CreateLogger("Delibera.Core.Council"));
         return builder;
      }));

      return services;
   }

   /// <summary>
   ///    Registers a Microsoft.Extensions.AI <see cref="IChatClient" /> and exposes it as a Delibera
   ///    <see cref="ILLMProvider" /> (<see cref="ChatClientLLMProvider" />).
   /// </summary>
   /// <remarks>
   ///    Lets you wire any Microsoft.Extensions.AI backend (OpenAI, Azure OpenAI, Ollama, local
   ///    OpenAI-compatible servers) into the container and consume it through Delibera's provider
   ///    abstraction. The factory delegate may compose a middleware pipeline (function invocation,
   ///    logging, caching) before returning the client.
   /// </remarks>
   /// <param name="services">The service collection.</param>
   /// <param name="chatClientFactory">Factory that builds the chat client (optionally with middleware).</param>
   /// <param name="providerName">Optional friendly provider name surfaced by <see cref="ILLMProvider.ProviderName" />.</param>
   public static IServiceCollection AddDeliberaChatClient(
      this IServiceCollection services,
      Func<IServiceProvider, IChatClient> chatClientFactory,
      string? providerName = null)
   {
      ArgumentNullException.ThrowIfNull(chatClientFactory);

      services.AddDelibera();
      services.TryAddSingleton(chatClientFactory);
      // The DI container owns the IChatClient lifetime, so the provider must not dispose it.
      services.TryAddSingleton<ILLMProvider>(sp =>
         new ChatClientLLMProvider(sp.GetRequiredService<IChatClient>(), providerName, false));

      return services;
   }

   /// <summary>
   ///    Registers an already-constructed Microsoft.Extensions.AI <see cref="IChatClient" /> and exposes it
   ///    as a Delibera <see cref="ILLMProvider" />.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <param name="chatClient">The chat client instance.</param>
   /// <param name="providerName">Optional friendly provider name.</param>
   public static IServiceCollection AddDeliberaChatClient(
      this IServiceCollection services,
      IChatClient chatClient,
      string? providerName = null)
   {
      ArgumentNullException.ThrowIfNull(chatClient);
      return services.AddDeliberaChatClient(_ => chatClient, providerName);
   }

   /// <summary>
   ///    Registers a Microsoft.Extensions.AI <see cref="IEmbeddingGenerator{TInput,TEmbedding}" /> and exposes
   ///    it as a Delibera <see cref="IEmbeddingProvider" /> (<see cref="EmbeddingGeneratorProvider" />).
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <param name="generatorFactory">Factory that builds the embedding generator.</param>
   /// <param name="modelName">Optional friendly model name.</param>
   /// <param name="vectorSize">Optional known vector dimensionality.</param>
   public static IServiceCollection AddDeliberaEmbeddingGenerator(
      this IServiceCollection services,
      Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>> generatorFactory,
      string? modelName = null,
      int? vectorSize = null)
   {
      ArgumentNullException.ThrowIfNull(generatorFactory);

      services.AddDelibera();
      services.TryAddSingleton(generatorFactory);
      services.TryAddSingleton<IEmbeddingProvider>(sp =>
         new EmbeddingGeneratorProvider(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            modelName,
            vectorSize,
            false));

      return services;
   }
}