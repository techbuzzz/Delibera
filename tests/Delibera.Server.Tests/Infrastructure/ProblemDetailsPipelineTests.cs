using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Delibera.Server.Tests.Infrastructure;

/// <summary>
///    W4-02 / W4-03 — the error pipeline, exercised against the real host.
///    <para>
///    These are the checks that static reading cannot do: whether the host actually starts
///    with the Serilog and ProblemDetails wiring, and whether an unhandled exception comes
///    back as an RFC 7807 body rather than a bare 500.
///    </para>
/// </summary>
public sealed class ProblemDetailsPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProblemDetailsPipelineTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> Production() =>
        _factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));

    [Fact]
    public async Task Host_Starts_With_The_Serilog_And_ProblemDetails_Wiring()
    {
        // If UseSerilog or AddProblemDetails were misconfigured, the host would throw here
        // rather than at the first request.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unmatched_Route_Returns_A_ProblemDetails_Body()
    {
        using var client = Production().CreateClient();

        var response = await client.GetAsync("/api/v1/there-is-no-such-endpoint");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("\"type\"",
            "a 404 with no body is not something a client can parse");
    }

    [Fact]
    public async Task ProblemDetails_Carries_The_CorrelationId_Header()
    {
        using var client = Production().CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "test-correlation-id");

        var response = await client.GetAsync("/api/v1/there-is-no-such-endpoint");
        var body = await response.Content.ReadAsStringAsync();

        response.Headers.GetValues("X-Correlation-Id").Should().Contain("test-correlation-id");
        body.Should().Contain("test-correlation-id",
            "the id has to be quotable by the caller, otherwise the log entry is unreachable");
    }

    [Fact]
    public async Task Exception_Detail_Is_Not_Leaked_In_Production()
    {
        // A debate with an unknown template makes the orchestration service throw. In
        // Production the payload must not contain the exception message.
        using var client = Production().CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/debates",
            new { templateId = "code-review", question = "why?" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body.Should().NotContain("code-review",
            "internal identifiers and messages stay in the log, not in the response");
        body.Should().NotContain("Exception", because: "no type name of an internal type belongs in a response");
    }

    [Fact]
    public async Task Exception_Detail_Is_Present_In_Development()
    {
        // The counterpart: a developer needs the message locally, and the handler is
        // explicit about limiting it to Development rather than relying on the framework's
        // per-environment default.
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/debates",
            new { templateId = "code-review", question = "why?" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("detail", out _).Should().BeTrue(
            $"expected a detail field in Development, got: {body}");
    }

    [Fact]
    public async Task ProblemDetails_Response_Is_Json()
    {
        using var client = Production().CreateClient();

        var response = await client.GetAsync("/api/v1/there-is-no-such-endpoint");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
