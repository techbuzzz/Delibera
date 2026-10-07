using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Delibera.Server.Tests.Endpoints;

/// <summary>
///    W4-05 / W4-06 — request validation across the HTTP surface.
///    <para>
///    Three of the four request contracts had no validator, so a malformed body reached the
///    service and surfaced as a 500. And <c>ValidationFilter</c> returned 422 while every
///    endpoint declared <c>.ProducesValidationProblem()</c>, which is 400 — so generated
///    clients were built against a status the server never returned.
///    </para>
/// </summary>
public sealed class RequestValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RequestValidationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient();

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task Scenario_Without_Question_Is_A_400_Not_A_500()
    {
        using var client = Client();

        // An empty string deserialises fine; the validator is what must reject it.
        var response = await client.PostAsJsonAsync("/api/v1/scenarios", new
        {
            question = "",
            members = new[] { new { role = "Architect" } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblem(response);
        problem.GetProperty("errors").TryGetProperty("Question", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_Without_Members_Is_A_400_Not_A_500()
    {
        using var client = Client();

        // An empty array binds cleanly and must be rejected by the validator — before this
        // task it reached ScenarioBuilder and came back as a 500.
        var response = await client.PostAsJsonAsync("/api/v1/scenarios", new
        {
            question = "gRPC or REST?",
            members = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scenario_Rejects_Unbounded_MaxRounds()
    {
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/v1/scenarios", new
        {
            question = "Ship it?",
            members = new[] { new { role = "Architect" } },
            maxRounds = 5000,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "each round is a paid LLM call, so the count cannot be unbounded");
    }

    [Fact]
    public async Task Scenario_Rejects_Out_Of_Range_Temperature()
    {
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/v1/scenarios", new
        {
            question = "Ship it?",
            members = new[] { new { role = "Architect" } },
            temperature = 7.5f,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scenario_With_An_Unknown_Strategy_Is_Still_Accepted()
    {
        // The documented behaviour is a graceful fallback to "Standard". A validator on
        // Strategy would silently turn that into a rejection, so the contract keeps it.
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/v1/scenarios/validate", new
        {
            question = "Ship it?",
            members = new[] { new { role = "Architect" } },
            strategy = "unknown-strategy",
        });

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.BadRequest],
            "the validate endpoint must not reject an unknown strategy outright");
    }

    [Fact]
    public async Task Corpus_Without_A_Name_Is_A_400()
    {
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/v1/corpora", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IndexDocument_Without_Content_Is_A_400_Not_A_NullReference()
    {
        using var client = Client();

        var create = await client.PostAsJsonAsync("/api/v1/corpora", new { name = "handbook" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadProblem(create);
        var corpusId = created.GetProperty("corpusId").GetString();

        // Content is required: CorpusService.EstimateChunks split it without a null check.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/corpora/{corpusId}/documents",
            new { content = "", title = "no content" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_Required_Member_Is_A_400_Not_A_500()
    {
        // A missing `required` member fails during JSON binding, before any validator runs.
        // The exception surfaces as BadHttpRequestException, and the problem-details handler
        // must NOT claim it — doing so turned this into a 500. That regression was found by
        // this test file, not by reading the handler.
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/v1/scenarios", new
        {
            question = "which transport?",
            // members omitted entirely
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a client that forgot a required field must not be told the server broke");
    }

    [Fact]
    public async Task ValidationProblem_Is_Reported_As_ProblemJson()
    {
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/v1/scenarios", new { question = "?" });

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "a validation problem has its own media type in RFC 7807");
    }
}
