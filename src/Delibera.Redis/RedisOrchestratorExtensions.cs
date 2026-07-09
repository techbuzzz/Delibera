using Delibera.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
}