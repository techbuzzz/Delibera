using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Legal;
using Delibera.Server.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Delibera.Server.Tests.Templates;

public sealed class LegalContractReviewTemplateTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();
    private static readonly IConfiguration   Config  = FakeConfiguration.Default();

    [Fact]
    public void TemplateId_IsLegalContractReview()
        => new LegalContractReviewTemplate().TemplateId.Should().Be("legal-contract-review");

    [Fact]
    public void Configure_WithMinimalRequest_DoesNotThrow()
    {
        var act = () => new LegalContractReviewTemplate().Configure(
            new CreateDebateRequest { TemplateId = "legal-contract-review", Question = "Is this NDA clause acceptable?" },
            Services, Config);
        act.Should().NotThrow();
    }

    [Fact]
    public void Configure_WithContractTextInputData_DoesNotThrow()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            "{\"contractText\":\"This agreement is entered into...\",\"jurisdiction\":\"RU\"}");
        var act = () => new LegalContractReviewTemplate().Configure(
            new CreateDebateRequest
            {
                TemplateId = "legal-contract-review",
                Question   = "Review this clause",
                InputData  = doc.RootElement,
            },
            Services, Config);
        act.Should().NotThrow();
    }
}
