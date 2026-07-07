using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Scenarios;

/// <summary>
/// Converts a <see cref="ScenarioRequest"/> into a fully-configured
/// <see cref="CouncilBuilder"/> ready for execution.
/// </summary>
public static class ScenarioBuilder
{
    public static CouncilBuilder Build(
        ScenarioRequest request,
        IConfiguration  configuration)
    {
        if (request.Members is not { Length: > 0 })
            throw new ArgumentException("A scenario must have at least one member.", nameof(request));

        // ── Provider resolution ───────────────────────────────────────────────
        var endpoint    = configuration["Delibera:Providers:DefaultEndpoint"] ?? "http://localhost:11434";
        var apiKey      = configuration["Delibera:Providers:ApiKey"];
        var fastModel   = configuration["Delibera:Models:Fast"]   ?? "llama3.2:3b";
        var strongModel = configuration["Delibera:Models:Strong"] ?? "qwen2.5:7b";
        var factory     = new ProviderFactory();

        ILLMProvider DefaultProvider() =>
            string.IsNullOrEmpty(apiKey)
                ? factory.CreateOllama(endpoint)
                : factory.CreateOllamaCloud(apiKey);

        ILLMProvider ResolveProvider(string? providerType) =>
            providerType?.ToLowerInvariant() switch
            {
                "openai"      => factory.CreateOpenAI(apiKey ?? string.Empty),
                "azureopenai" => factory.CreateAzureOpenAI(apiKey ?? string.Empty, endpoint),
                "anthropic"   => factory.CreateAnthropic(apiKey ?? string.Empty),
                _             => DefaultProvider(),
            };

        string ResolveModel(string? model, bool preferStrong = true) =>
            !string.IsNullOrWhiteSpace(model) ? model
                : preferStrong ? strongModel : fastModel;

        // ── Builder base ──────────────────────────────────────────────────────
        var builder = new CouncilBuilder()
            .WithMaxRounds(request.MaxRounds)
            .WithTemperature(request.Temperature);

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            builder.WithSystemPrompt(request.SystemPrompt);
        else
        {
            var ctx = request.InputData.HasValue
                ? request.InputData.Value.ToString()
                : string.Empty;
            builder.WithSystemPrompt(
                string.IsNullOrWhiteSpace(ctx)
                    ? request.Question
                    : $"{request.Question}\n\nContext:\n{ctx}");
        }

        builder.WithUserPrompt(request.Question);

        // ── Members ───────────────────────────────────────────────────────────
        foreach (var m in request.Members)
        {
            var model    = ResolveModel(m.Model);
            var provider = ResolveProvider(m.Provider);
            var caps     = m.Capabilities.ToLowerInvariant() switch
            {
                "vision" => MemberCapabilities.Vision,
                "both"   => MemberCapabilities.Text | MemberCapabilities.Vision,
                _        => MemberCapabilities.Text,
            };
            builder.AddMember(model, provider, m.Role, caps, m.Persona);
        }

        // ── Strategy ──────────────────────────────────────────────────────────
        _ = request.Strategy.ToLowerInvariant() switch
        {
            "critique"  => builder.WithCritiqueDebate(),
            "consensus" => builder.WithConsensusDebate(),
            _           => builder.WithStandardDebate(),
        };

        // ── Chairman ──────────────────────────────────────────────────────────
        if (request.Chairman is { } ch)
        {
            var chModel    = ResolveModel(ch.Model, preferStrong: true);
            var chProvider = ResolveProvider(ch.Provider);
            var chairman   = ch.OpeningStatement
                ? Chairman.CreateWithOpening(chModel, chProvider, ch.SystemPrompt)
                : Chairman.CreateStandard(chModel, chProvider, ch.SystemPrompt);
            builder.SetChairman(chairman);
        }

        // ── Voting ────────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(request.VotingStrategy))
        {
            IVotingStrategy voting = request.VotingStrategy.ToLowerInvariant() switch
            {
                "bordacount" or "borda" =>
                    new BordaCountVotingStrategy(),
                "weighted" =>
                    new WeightedVotingStrategy(
                        request.MemberWeights
                        ?? request.Members.ToDictionary(m => m.Role, m => m.Weight)),
                _ =>
                    new MajorityVotingStrategy(),
            };
            builder.WithVoting(voting);
        }

        // ── Knowledge text (inline RAG) ───────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(request.KnowledgeText))
            builder.WithKnowledgeText(request.KnowledgeText);

        // ── Compression ───────────────────────────────────────────────────────
        if (!string.Equals(request.CompressionStrategy, "None", StringComparison.OrdinalIgnoreCase))
        {
            var embeddingModel = configuration["Delibera:Providers:EmbeddingModel"] ?? "nomic-embed-text";
            var llm            = DefaultProvider();
            var embeddings     = new Delibera.Core.Providers.RAG.OllamaEmbeddingProvider(
                (Delibera.Core.Providers.LLM.OllamaProvider)llm, embeddingModel);
            if (Enum.TryParse<Delibera.Core.Compression.CompressionStrategy>(
                    request.CompressionStrategy, true, out var cs))
                builder.WithCompression(cs, llm, strongModel, embeddings);
        }

        return builder;
    }
}
