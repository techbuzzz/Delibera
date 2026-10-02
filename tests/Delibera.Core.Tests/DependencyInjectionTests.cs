using Delibera.Core.Caching;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the DI extension surface (W5-02).
///    These helpers are the entry point for every consumer of the library, so a defect
///    here stays invisible until the first real debate in production — which is exactly
///    how W1-02 (UseInMemoryCache threw because AddMemoryCache was never registered)
///    survived into a release.
/// </summary>
public sealed class DependencyInjectionTests
{
    private static readonly DebateResult _sampleResult = new()
    {
        StrategyName = "Standard",
        Context = new PromptContext(),
        Participants = ["Alice"],
        FinalVerdict = "Yes",
    };

    [Fact]
    public void UseInMemoryCache_Resolves_Without_A_Manual_AddMemoryCache()
    {
        // Regression guard for W1-02: the factory resolves IMemoryCache lazily, so a
        // missing registration only throws at first use — long after the container was
        // built and only on the first cached debate.
        var services = new ServiceCollection();
        services.AddDelibera().UseInMemoryCache();

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IDebateCache>();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task UseInMemoryCache_RoundTrips_A_Result()
    {
        var services = new ServiceCollection();
        services.AddDelibera().UseInMemoryCache();

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDebateCache>();

        await cache.SetAsync("di-key", _sampleResult);
        var result = await cache.GetAsync("di-key");

        result.Should().NotBeNull();
        result!.FinalVerdict.Should().Be("Yes");
    }

    [Fact]
    public void UseInMemoryCache_Does_Not_Replace_A_Host_Registered_Cache()
    {
        // AddMemoryCache is idempotent and the cache itself is TryAddSingleton, so a host
        // that registered its own IDebateCache must keep it.
        var services = new ServiceCollection();
        var custom = new FileDebateCacheProbe();
        services.AddDelibera();
        services.AddSingleton<IDebateCache>(custom);
        services.UseInMemoryCache();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDebateCache>().Should().BeSameAs(custom);
    }

    [Fact]
    public void AddDelibera_Registers_The_Documented_Core_Abstractions()
    {
        var services = new ServiceCollection();
        services.AddDelibera();

        using var provider = services.BuildServiceProvider();

        provider.GetService<ILLMProviderFactory>().Should().NotBeNull();
        provider.GetService<IVectorStoreFactory>().Should().NotBeNull();
        provider.GetService<ICompressionFactory>().Should().NotBeNull();
        provider.GetService<ICouncilBuilder>().Should().NotBeNull();
        provider.GetService<IDebateOrchestrator>().Should().NotBeNull();
    }

    /// <summary>Minimal IDebateCache stand-in used to prove registration precedence.</summary>
    private sealed class FileDebateCacheProbe : IDebateCache
    {
        public ValueTask<DebateResult?> GetAsync(string key, CancellationToken ct = default)
            => ValueTask.FromResult<DebateResult?>(null);

        public ValueTask SetAsync(string cacheKey, DebateResult result, TimeSpan? ttl = null, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask InvalidateAsync(string key, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default)
            => ValueTask.FromResult(false);
    }
}
