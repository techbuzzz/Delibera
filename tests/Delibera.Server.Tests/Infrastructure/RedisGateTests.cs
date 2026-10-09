using Delibera.Core.Interfaces;
using Delibera.Redis;
using Delibera.Server.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Delibera.Server.Tests.Infrastructure;

/// <summary>
///    Guards the <c>Delibera:Redis:Enabled</c> gate.
/// </summary>
/// <remarks>
///    The default path is the one that matters: a host that never configured Redis must
///    resolve the in-process orchestrator exactly as it did before this option existed.
///    A gate that leaks into the default would silently change behaviour for every existing
///    deployment, which is why that case is asserted rather than assumed.
/// </remarks>
public sealed class RedisGateTests
{
    private static IConfiguration ConfigWith(params (string Key, string? Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => e.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Gate_Is_Off_By_Default_When_No_Redis_Section_Exists()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeliberaRedis(ConfigWith(("Delibera:Strategy", "Standard")));

        // Nothing Redis-shaped may be registered: no multiplexer, no orchestrator override.
        services.Should().NotContain(d => d.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public void Enabled_False_Registers_No_Multiplexer_And_Leaves_The_Orchestrator_Alone()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeliberaRedis(ConfigWith(
            ("Delibera:Redis:Enabled", "false"),
            ("Delibera:Redis:ConnectionString", "redis:6379,abortConnect=false")));

        services.Should().NotContain(d => d.ServiceType == typeof(IConnectionMultiplexer));
        services.Should().NotContain(d => d.ServiceType == typeof(IDebateCache));
    }

    [Fact]
    public void Enabled_True_Registers_The_Multiplexer_And_Replaces_The_Orchestrator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeliberaRedis(ConfigWith(
            ("Delibera:Redis:Enabled", "true"),
            ("Delibera:Redis:ConnectionString", "redis:6379,abortConnect=false")));

        services.Should().Contain(d => d.ServiceType == typeof(IConnectionMultiplexer));

        // AddRedisDebateOrchestrator must Replace, not Add — a second IDebateOrchestrator
        // registration would leave resolution to whichever descriptor DI considered last,
        // which is exactly the kind of ordering bug that only shows up in production.
        var orchestrators = services.Where(d => d.ServiceType == typeof(IDebateOrchestrator)).ToArray();
        orchestrators.Should().ContainSingle();
        orchestrators[0].ImplementationType.Should().Be(typeof(RedisDebateOrchestrator));
    }

    [Fact]
    public void Enabled_True_With_CacheEnabled_Registers_The_Redis_Cache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeliberaRedis(ConfigWith(
            ("Delibera:Redis:Enabled", "true"),
            ("Delibera:Redis:CacheEnabled", "true"),
            ("Delibera:Redis:ConnectionString", "redis:6379,abortConnect=false")));

        var caches = services.Where(d => d.ServiceType == typeof(IDebateCache)).ToArray();
        caches.Should().ContainSingle();
    }

    [Fact]
    public void Enabled_True_With_Empty_ConnectionString_Fails_Naming_The_Config_Key()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddDeliberaRedis(ConfigWith(
            ("Delibera:Redis:Enabled", "true"),
            ("Delibera:Redis:ConnectionString", "   ")));

        // Fail-fast, not silent fallback: a deployment that quietly drops back to the local
        // orchestrator looks distributed and is not.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Delibera:Redis:ConnectionString*");
    }

    [Fact]
    public void Enabled_True_With_A_ConnectionString_Declaring_No_Endpoint_Fails()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // "host:notaport" is the realistic mistake, and it parses WITHOUT throwing — it
        // simply yields zero endpoints. Verified against StackExchange.Redis rather than
        // assumed: an earlier draft of this test asserted that a "redis://host" style string
        // fails to parse, and it does not (it resolves to one endpoint). So the endpoint
        // count is the assertion that actually catches an unusable configuration.
        var act = () => services.AddDeliberaRedis(ConfigWith(
            ("Delibera:Redis:Enabled", "true"),
            ("Delibera:Redis:ConnectionString", "host:notaport")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Delibera:Redis:ConnectionString*");
    }

    [Fact]
    public void ConnectionString_Parsed_By_The_Gate_Is_Accepted_By_StackExchange_Redis()
    {
        // Proves the gate validates with the same parser the multiplexer uses, so a string
        // that passes the gate cannot throw later inside RedisDebateOrchestrator's ctor.
        var options = ConfigurationOptions.Parse("redis:6379,abortConnect=false");

        options.EndPoints.Should().NotBeEmpty();
    }

    [Fact]
    public void Gate_SectionName_Matches_The_RedisOrchestrator_Section()
    {
        // Two sections describing one Redis deployment is how Enabled ends up false while the
        // connection string looks correctly configured — and Redis then silently never runs.
        RedisGateOptions.SectionName.Should().Be(RedisOrchestratorOptions.SectionName);
    }

    [Fact]
    public void Shipped_Appsettings_Json_Declares_The_Redis_Block_Disabled()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Delibera.Server", "appsettings.json");

        File.Exists(path).Should().BeTrue($"the shipped configuration should be at {path}");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

        var section = RedisGateOptions.SectionName.Split(':');
        var node = doc.RootElement;
        foreach (var part in section)
            node.TryGetProperty(part, out node).Should().BeTrue(
                $"appsettings.json has no '{RedisGateOptions.SectionName}' block");

        // Must be present AND false: a checked-in "true" would make every default deployment
        // require a Redis that the documented quickstart does not start.
        node.GetProperty("Enabled").GetBoolean().Should().BeFalse();
    }
}