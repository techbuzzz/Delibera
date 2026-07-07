using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Engineering;
using Delibera.Server.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Delibera.Server.Tests.Templates;

public sealed class CodeReviewTemplateTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();
    private static readonly IConfiguration   Config  = FakeConfiguration.Default();

    [Fact]
    public void TemplateId_IsCodeReview()
        => new CodeReviewTemplate().TemplateId.Should().Be("code-review");

    [Fact]
    public void Configure_WithMinimalRequest_ReturnsNonNullBuilder()
    {
        var builder = new CodeReviewTemplate().Configure(
            new CreateDebateRequest { TemplateId = "code-review", Question = "Review this PR" },
            Services, Config);
        builder.Should().NotBeNull();
    }

    [Fact]
    public void Configure_WithMinimalRequest_DoesNotThrow()
    {
        var act = () => new CodeReviewTemplate().Configure(
            new CreateDebateRequest { TemplateId = "code-review", Question = "Approve?" },
            Services, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Configure_WithInputData_DoesNotThrow()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            "{\"diff\":\"+ added line\",\"files\":[\"Program.cs\"]}");
        var act = () => new CodeReviewTemplate().Configure(
            new CreateDebateRequest
            {
                TemplateId = "code-review",
                Question   = "Review the diff",
                InputData  = doc.RootElement,
            },
            Services, Config);
        act.Should().NotThrow();
    }
}
