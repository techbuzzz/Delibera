using System.Text.Json;
using System.Text.Json.Nodes;
using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Output;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-05 Structured Output / JSON Schema feature
///    (<see cref="IStructuredOutputSerializer"/>, <see cref="JsonSchemaOutputSerializer"/>,
///    <see cref="DebateResult.GetTypedVerdict{TVerdict}"/>,
///    <see cref="ICouncilBuilder.WithStructuredOutput{TVerdict}"/>,
///    <see cref="ICouncilExecutor.ExecuteTypedAsync{TVerdict}"/>).
/// </summary>
public class StructuredOutputTests
{
    // ── Serializer unit tests ──

    [Fact]
    public void JsonSchemaOutputSerializer_Generates_Schema_With_Properties()
    {
        var serializer = new JsonSchemaOutputSerializer();
        var schema = serializer.GenerateSchema<ArchitectureDecision>();
        schema.Should().NotBeNullOrWhiteSpace();
        schema.Should().Contain("\"properties\"");
        schema.Should().Contain("recommendation");
        schema.Should().Contain("confidence");
        schema.Should().Contain("risks");
        schema.Should().Contain("benefits");
    }

    [Fact]
    public void JsonSchemaOutputSerializer_Deserializes_Valid_Json()
    {
        var serializer = new JsonSchemaOutputSerializer();
        var json = """{"recommendation":"Migrate","confidence":0.85,"risks":[],"benefits":["scalability"],"rationale":"Growth."}""";
        var verdict = serializer.Deserialize<ArchitectureDecision>(json);
        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Migrate");
        verdict.Confidence.Should().Be(0.85);
        verdict.Benefits.Should().Contain("scalability");
    }

    [Fact]
    public void JsonSchemaOutputSerializer_Extracts_Json_From_Markdown_Fence()
    {
        var text = """
                   Here is the verdict:

                   ```json
                   {"recommendation":"Stay","confidence":0.7,"risks":["complexity"],"benefits":[],"rationale":"Simpler."}
                   ```
                   """;
        var serializer = new JsonSchemaOutputSerializer();
        var verdict = serializer.Deserialize<ArchitectureDecision>(text);
        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Stay");
    }

    [Fact]
    public void JsonSchemaOutputSerializer_Extracts_Json_From_Prose_Wrapper()
    {
        var text = "After deliberation, the verdict is {\"recommendation\":\"Migrate\",\"confidence\":0.9,\"risks\":[],\"benefits\":[\"scale\"],\"rationale\":\"ok\"} — thank you.";
        var serializer = new JsonSchemaOutputSerializer();
        var verdict = serializer.Deserialize<ArchitectureDecision>(text);
        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Migrate");
    }

    [Fact]
    public void JsonSchemaOutputSerializer_Throws_On_No_Json()
    {
        var serializer = new JsonSchemaOutputSerializer();
        var act = () => serializer.Deserialize<ArchitectureDecision>("just prose, no json here");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void JsonSchemaOutputSerializer_Missing_Properties_Yield_Defaults()
    {
        // Records with positional parameters have defaults (null/0). System.Text.Json
        // silently uses defaults when properties are absent — this is expected
        // behaviour for permissive deserialisation.
        var serializer = new JsonSchemaOutputSerializer();
        var json = """{"recommendation":"Migrate"}""";
        var verdict = serializer.Deserialize<ArchitectureDecision>(json);
        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Migrate");
        verdict.Confidence.Should().Be(0.0);
        verdict.Risks.Should().BeNull();
    }

    [Fact]
    public void JsonSchemaOutputSerializer_PropertyName_Case_Insensitive()
    {
        var serializer = new JsonSchemaOutputSerializer();
        var json = """{"Recommendation":"Migrate","Confidence":0.5,"Risks":[],"Benefits":[],"Rationale":"x"}""";
        var verdict = serializer.Deserialize<ArchitectureDecision>(json);
        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Migrate");
    }

    [Fact]
    public void BuildStructuredPrompt_Includes_Schema_And_TypeName()
    {
        var schema = "{\"type\":\"object\"}";
        var prompt = JsonSchemaOutputSerializer.BuildStructuredPrompt("base", schema, "MyType");
        prompt.Should().Contain("base");
        prompt.Should().Contain(schema);
        prompt.Should().Contain("MyType");
        prompt.Should().Contain("STRUCTURED OUTPUT REQUIRED");
    }

    [Fact]
    public void BuildCorrectionPrompt_Includes_Error_And_Schema()
    {
        var prompt = JsonSchemaOutputSerializer.BuildCorrectionPrompt("bad response", "missing field", "{}", "MyType");
        prompt.Should().Contain("bad response");
        prompt.Should().Contain("missing field");
        prompt.Should().Contain("MyType");
    }

    // ── CouncilBuilder integration ──

    [Fact]
    public void CouncilBuilder_WithStructuredOutput_Stamps_Serializer_And_Type()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .SetChairman("chairman", provider)
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithStructuredOutput<ArchitectureDecision>()
            .Build();

        executor.StructuredOutputSerializer.Should().NotBeNull();
        executor.StructuredOutputType.Should().Be<ArchitectureDecision>();
    }

    [Fact]
    public void CouncilBuilder_WithoutStructuredOutput_Leaves_Serializer_Null()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.StructuredOutputSerializer.Should().BeNull();
        executor.StructuredOutputType.Should().BeNull();
    }

    [Fact]
    public void CouncilBuilder_WithStructuredOutput_Custom_Serializer_Is_Used()
    {
        var provider = new FakeLLMProvider();
        var custom = new JsonSchemaOutputSerializer(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithStructuredOutput<ArchitectureDecision>(custom)
            .Build();

        executor.StructuredOutputSerializer.Should().BeSameAs(custom);
    }

    // ── DebateResult.GetTypedVerdict ──

    [Fact]
    public void GetTypedVerdict_Returns_Null_When_FinalVerdict_Empty()
    {
        var result = new DebateResult
        {
            StrategyName = "s",
            Context = new PromptContext { SystemPrompt = "s", UserPrompt = "q" },
            Participants = ["A"]
        };
        result.GetTypedVerdict<ArchitectureDecision>().Should().BeNull();
    }

    [Fact]
    public void GetTypedVerdict_Deserialises_FinalVerdict_When_Structured()
    {
        var result = new DebateResult
        {
            StrategyName = "s",
            Context = new PromptContext { SystemPrompt = "s", UserPrompt = "q" },
            Participants = ["A"],
            FinalVerdict = """{"recommendation":"Ship","confidence":0.9,"risks":[],"benefits":["speed"],"rationale":"ok"}"""
        };
        var verdict = result.GetTypedVerdict<ArchitectureDecision>();
        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Ship");
    }

    [Fact]
    public void GetTypedVerdict_Returns_Cached_TypedVerdict_When_Available()
    {
        var cached = new ArchitectureDecision("Cached", 0.5, [], [], "from cache");
        var result = new DebateResult
        {
            StrategyName = "s",
            Context = new PromptContext { SystemPrompt = "s", UserPrompt = "q" },
            Participants = ["A"],
            FinalVerdict = "invalid json",
            TypedVerdict = cached
        };
        result.GetTypedVerdict<ArchitectureDecision>().Should().BeSameAs(cached);
    }

    [Fact]
    public void GetTypedVerdict_Returns_Null_When_Deserialisation_Fails()
    {
        var result = new DebateResult
        {
            StrategyName = "s",
            Context = new PromptContext { SystemPrompt = "s", UserPrompt = "q" },
            Participants = ["A"],
            FinalVerdict = "not valid json at all"
        };
        result.GetTypedVerdict<ArchitectureDecision>().Should().BeNull();
    }

    // ── ExecuteTypedAsync integration ──

    [Fact]
    public async Task ExecuteTypedAsync_Without_StructuredOutput_Returns_Verdict_If_Present()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .SetChairman("chairman", provider)
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();
        result.Should().NotBeNull();
        verdict.Should().BeNull(); // FinalVerdict is "ok" or similar, won't parse
    }

    [Fact]
    public async Task ExecuteTypedAsync_With_StructuredOutput_Deserialises_Synthetic_Reply()
    {
        // Provider returns a valid JSON verdict when asked. The Chairman synthesis
        // prompt will be the structured prompt, so the reply will be the JSON.
        var verdictJson = """{"recommendation":"Migrate","confidence":0.85,"risks":[],"benefits":["scale"],"rationale":"Growth."}""";
        var provider = new FakeLLMProvider(reply: verdictJson);
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .SetChairman("chairman", provider)
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithStructuredOutput<ArchitectureDecision>()
            .Build();

        var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();

        result.Should().NotBeNull();
        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Migrate");
        verdict.Confidence.Should().Be(0.85);
        result.TypedVerdict.Should().BeSameAs(verdict);
    }

    [Fact]
    public async Task ExecuteTypedAsync_Retries_On_Deserialisation_Failure()
    {
        // First reply is invalid JSON (used as initial FinalVerdict by the strategy).
        // Second reply (on retry) is valid JSON.
        var validJson = """{"recommendation":"Migrate","confidence":0.7,"risks":[],"benefits":[],"rationale":"ok"}""";
        var provider = new TwoPhaseLLMProvider(
            initialReply: "not json at all, just prose about the topic",
            retryReply: validJson);
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .SetChairman("chairman", provider)
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithStructuredOutput<ArchitectureDecision>()
            .Build();

        var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();

        verdict.Should().NotBeNull();
        verdict!.Recommendation.Should().Be("Migrate");
        result.FinalVerdict.Should().Be(validJson);
    }

    [Fact]
    public async Task ExecuteTypedAsync_No_Chairman_Returns_Null_Verdict()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            // No Chairman → no retry possible
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithStructuredOutput<ArchitectureDecision>()
            .Build();

        var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();
        result.Should().NotBeNull();
        verdict.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteTypedAsync_Logs_Warning_When_Retry_Fails()
    {
        var provider = new TwoPhaseLLMProvider(
            initialReply: "bad json",
            retryReply: "still not json");
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .SetChairman("chairman", provider)
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithStructuredOutput<ArchitectureDecision>()
            .Build();

        var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();
        verdict.Should().BeNull();
        result.ExecutionLogs.Should().Contain(l => l.Message.Contains("Deserialisation failed"));
    }

    // ── Test fixtures ──

    public sealed record ArchitectureDecision(
        string Recommendation,
        double Confidence,
        IReadOnlyList<string> Risks,
        IReadOnlyList<string> Benefits,
        string Rationale);

    /// <summary>
    ///    Fake provider that returns <c>initialReply</c> for all calls during the
    ///    standard debate rounds, then <c>retryReply</c> when the structured-output
    ///    correction prompt is sent (detected by the "could not be parsed" marker).
    /// </summary>
    private sealed class TwoPhaseLLMProvider(string initialReply, string retryReply) : ILLMProvider
    {
        private int _callCount;
        public string ProviderName => "TwoPhase";
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(["fake"]);
        public Task<string> ChatAsync(string model, string systemPrompt, string userPrompt, float temperature = 0.7f, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            // The structured-output correction prompt contains "could not be parsed".
            if (userPrompt.Contains("could not be parsed", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(retryReply);
            return Task.FromResult(initialReply);
        }
        public Task<ModelCapabilities> GetModelCapabilitiesAsync(string model, CancellationToken ct = default)
            => Task.FromResult(ModelCapabilities.Unknown(model));
        public void Dispose() { }
    }
}