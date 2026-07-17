using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Product;
using Delibera.Server.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Delibera.Server.Tests.Templates;

public sealed class RequirementsReviewTemplateTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();
    private static readonly IConfiguration   Config  = FakeConfiguration.Default();

    [Fact]
    public void TemplateId_IsRequirementsReview()
        => new RequirementsReviewTemplate().TemplateId.Should().Be("requirements-review");

    [Fact]
    public void Configure_WithMinimalRequest_ReturnsNonNullBuilder()
    {
        var builder = new RequirementsReviewTemplate().Configure(
            new CreateDebateRequest { TemplateId = "requirements-review", Question = "Are these stories complete?" },
            Services, Config);
        builder.Should().NotBeNull();
    }

    [Fact]
    public void Configure_WhenInputDataIsNull_DoesNotThrow()
    {
        var act = () => new RequirementsReviewTemplate().Configure(
            new CreateDebateRequest
            {
                TemplateId = "requirements-review",
                Question   = "Validate requirements",
                InputData  = null,
            },
            Services, Config);
        act.Should().NotThrow();
    }
}
