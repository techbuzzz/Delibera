using Delibera.Core.Compression;
using Delibera.Core.Council;
using Delibera.Core.Providers;
using Delibera.Core.Providers.RAG;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Delibera.Core.DependencyInjection;

/// <summary>
/// Extension methods for registering Delibera services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
   /// <summary>
   /// Registers core Delibera services with default options.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <returns>The service collection for chaining.</returns>
   /// <remarks>
   /// Registers:
   /// <list type="bullet">
   ///   <item><see cref="ILLMProviderFactory"/> → <see cref="ProviderFactory"/> (singleton)</item>
   ///   <item><see cref="IRagProviderFactory"/> → <see cref="RagProviderFactory"/> (singleton)</item>
   ///   <item><see cref="ICompressionFactory"/> → <see cref="CompressionService"/> (singleton)</item>
   ///   <item><see cref="ICouncilBuilder"/> → <see cref="CouncilBuilder"/> (transient)</item>
   /// </list>
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
   /// Registers core Delibera services and binds <see cref="CouncilOptions"/> from configuration.
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
      if (section.Exists())
      {
         services.Configure<CouncilOptions>(section);
      }

      return services;
   }

   /// <summary>
   /// Registers core Delibera services with a custom options configuration delegate.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <param name="configureOptions">Delegate to configure <see cref="CouncilOptions"/>.</param>
   /// <returns>The service collection for chaining.</returns>
   public static IServiceCollection AddDelibera(
       this IServiceCollection services,
       Action<CouncilOptions> configureOptions)
   {
      services.AddDelibera();
      services.Configure(configureOptions);

      return services;
   }
}
