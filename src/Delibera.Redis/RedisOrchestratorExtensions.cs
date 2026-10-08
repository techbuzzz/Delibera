using Delibera.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Delibera.Redis;

public static class RedisOrchestratorExtensions
{
   /// <summary>
   ///    Registers <see cref="RedisDebateOrchestrator" /> as the <see cref="IDebateOrchestrator" /> implementation,
   ///    replacing the default <see cref="LocalDebateOrchestrator" />.
   /// </summary>
   public static IServiceCollection AddRedisDebateOrchestrator(
      this IServiceCollection services,
      IConfiguration configuration,
      string sectionName = RedisOrchestratorOptions.SectionName)
   {
      services.Configure<RedisOrchestratorOptions>(configuration.GetSection(sectionName));
      services.Replace(ServiceDescriptor.Singleton<IDebateOrchestrator, RedisDebateOrchestrator>());
      return services;
   }

   /// <summary>
   ///    Registers <see cref="RedisDebateOrchestrator" /> with inline configuration.
   /// </summary>
   public static IServiceCollection AddRedisDebateOrchestrator(
      this IServiceCollection services,
      Action<RedisOrchestratorOptions> configure)
   {
      services.Configure(configure);
      services.Replace(ServiceDescriptor.Singleton<IDebateOrchestrator, RedisDebateOrchestrator>());
      return services;
   }

   /// <summary>
   ///    Registers <see cref="RedisDebateCache" /> as the <see cref="IDebateCache" /> implementation
   ///    using the Redis connection from <see cref="RedisDebateOrchestrator" />.
   /// </summary>
   public static IServiceCollection UseRedisCache(
      this IServiceCollection services,
      TimeSpan? ttl = null)
   {
      services.TryAddSingleton<IDebateCache>(sp =>
      {
         var redis = sp.GetRequiredService<IConnectionMultiplexer>();
         var options = sp.GetRequiredService<IOptions<RedisOrchestratorOptions>>();
         var logger = sp.GetService<ILogger<RedisDebateCache>>();
         return new RedisDebateCache(redis, options, ttl, logger);
      });
      return services;
   }
}
