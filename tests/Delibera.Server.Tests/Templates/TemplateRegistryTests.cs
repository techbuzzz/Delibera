using Delibera.Server.Templates.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Delibera.Server.Tests.Templates;

public sealed class TemplateRegistryTests
{
    private static ITemplateRegistry BuildRegistry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITemplateRegistry, TemplateRegistry>();
        return services.BuildServiceProvider().GetRequiredService<ITemplateRegistry>();
    }

    [Fact]
    public void AllFiveTemplatesAreRegistered()
        => BuildRegistry().GetAll().Should().HaveCountGreaterThanOrEqualTo(5);

    [Theory]
    [InlineData("risk-committee")]
    [InlineData("architecture-decision")]
    [InlineData("code-review")]
    [InlineData("requirements-review")]
    [InlineData("legal-contract-review")]
    public void KnownTemplateIdsAreResolvable(string templateId)
    {
        var template = BuildRegistry().Get(templateId);
        template.Should().NotBeNull();
        template!.TemplateId.Should().Be(templateId);
    }

    [Fact]
    public void UnknownTemplateIdReturnsNull()
        => BuildRegistry().Get("does-not-exist").Should().BeNull();

    [Theory]
    [InlineData("risk-committee")]
    [InlineData("architecture-decision")]
    [InlineData("code-review")]
    [InlineData("requirements-review")]
    [InlineData("legal-contract-review")]
    public void EachTemplateHasNonEmptyDisplayName(string templateId)
        => BuildRegistry().Get(templateId)!.DisplayName.Should().NotBeNullOrWhiteSpace();
}
