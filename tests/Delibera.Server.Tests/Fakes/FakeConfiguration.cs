using Microsoft.Extensions.Configuration;

namespace Delibera.Server.Tests.Fakes;

/// <summary>
/// Builds a minimal <see cref="IConfiguration"/> for ScenarioBuilder and template tests
/// without any real appsettings file.
/// </summary>
public static class FakeConfiguration
{
    private static readonly Dictionary<string, string?> Defaults = new()
    {
        ["Delibera:Providers:DefaultEndpoint"] = "http://localhost:11434",
        ["Delibera:Providers:ApiKey"]          = null,
        ["Delibera:Providers:EmbeddingModel"]  = "nomic-embed-text",
        ["Delibera:Models:Fast"]               = "llama3.2:3b",
        ["Delibera:Models:Strong"]             = "qwen2.5:7b",
    };

    public static IConfiguration Default() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(Defaults)
            .Build();

    public static IConfiguration With(params (string key, string? value)[] overrides)
    {
        var dict = new Dictionary<string, string?>(Defaults);
        foreach (var (key, value) in overrides)
            dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }
}
