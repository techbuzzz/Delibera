using Delibera.Server.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Delibera.Server.Tests.Infrastructure;

/// <summary>
///    Regression guards for <see cref="DeliberaServerOptions" /> binding (W1-08).
/// </summary>
public sealed class DeliberaServerOptionsTests
{
    private static IConfiguration ConfigWith(params (string Key, string? Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => e.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void SectionName_Binds_The_Options_Block()
    {
        // SectionName used to be "DeliberaServer" while the configuration nests the block
        // under Delibera → Server. The mismatch was silent: the binder found nothing and
        // every option kept its default — including OtlpEndpoint, which is why the OTLP
        // exporter could never be switched on by configuration.
        var config = ConfigWith(
            ("Delibera:Server:DefaultTenantId", "acme"),
            ("Delibera:Server:MaxConcurrentDebates", "7"),
            ("Delibera:Server:DebateRetentionMinutes", "15"),
            ("Delibera:Server:Telemetry:OtlpEndpoint", "http://collector:4317"));

        var options = config.GetSection(DeliberaServerOptions.SectionName).Get<DeliberaServerOptions>();

        options.Should().NotBeNull();
        options!.DefaultTenantId.Should().Be("acme");
        options.MaxConcurrentDebates.Should().Be(7);
        options.DebateRetentionMinutes.Should().Be(15);
        options.Telemetry.OtlpEndpoint.Should().Be("http://collector:4317");
    }

    [Fact]
    public void SectionName_Matches_The_Shipped_Appsettings_Json()
    {
        // Binds the constant to the real file, so the two cannot drift apart again:
        // if appsettings.json is restructured, this test fails instead of the server
        // silently running on defaults.
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Delibera.Server", "appsettings.json");

        File.Exists(path).Should().BeTrue($"the shipped configuration should be at {path}");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var section = DeliberaServerOptions.SectionName.Split(':');

        var node = doc.RootElement;
        foreach (var part in section)
        {
            node.TryGetProperty(part, out node).Should().BeTrue(
                $"appsettings.json has no '{DeliberaServerOptions.SectionName}' block");
        }
    }
}
