using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Templates.Governance;

/// <summary>
/// Enterprise Risk Committee Council — Vertical 1: Enterprise Decision Support &amp; Governance.
///
/// Members:
///   RiskManager (weight 2.0)  — conservative, data-driven risk analysis
///   ComplianceOfficer (2.0)   — regulatory &amp; policy expert
///   Architect (1.5)           — technical feasibility
///   BusinessOwner (1.0)       — business value and velocity
///
/// Strategy : ConsensusDebate → WeightedVoting → Chairman synthesises verdict.
/// Output   : RiskVerdict (Recommendation, RiskLevel, Risks[], Rationale, Confidence)
/// </summary>
public sealed class RiskCommitteeTemplate : IServerTemplate
{
    public string   TemplateId       => "risk-committee";
    public string   DisplayName      => "Risk Committee Council";
    public string?  Description      => "Multi-role AI council for governance, risk assessment and policy decisions.";
    public string   Strategy         => "ConsensusDebate";
    public int      DefaultMaxRounds => 5;
    public string[] MemberRoles      => ["RiskManager", "ComplianceOfficer", "Architect", "BusinessOwner"];
    public string   VotingStrategy   => "Weighted";
    public bool     RagEnabled       => true;
    public bool     OperatorEnabled  => false;

    public CouncilBuilder Configure(
        CreateDebateRequest request,
        IServiceProvider    services,
        IConfiguration      configuration)
    {
        var providerSection = configuration.GetSection("Delibera:Providers");
        var endpoint        = providerSection["DefaultEndpoint"] ?? "http://localhost:11434";
        var apiKey          = providerSection["ApiKey"];
        var embeddingModel  = providerSection["EmbeddingModel"] ?? "nomic-embed-text";

        var factory  = new ProviderFactory();
        var llm      = string.IsNullOrEmpty(apiKey)
            ? factory.CreateOllama(endpoint)
            : factory.CreateOllamaCloud(apiKey);

        var fastModel   = configuration["Delibera:Models:Fast"]   ?? "llama3.2:3b";
        var strongModel = configuration["Delibera:Models:Strong"] ?? "qwen2.5:7b";

        var maxRounds = request.Options?.MaxRounds ?? DefaultMaxRounds;
        var temp      = request.Options?.Temperature ?? 0.7f;

        // Build weighted votes: override from request if provided
        var weights = request.Options?.MemberWeights ?? new Dictionary<string, float>
        {
            ["RiskManager"]        = 2.0f,
            ["ComplianceOfficer"]  = 2.0f,
            ["Architect"]          = 1.5f,
            ["BusinessOwner"]      = 1.0f,
        };

        var builder = new CouncilBuilder()
            .AddMember(strongModel, llm, "RiskManager",
                persona: "You are a seasoned risk manager. Analyse every proposal through the lens of operational, regulatory, financial and reputational risk. Be conservative and data-driven. Cite evidence.")
            .AddMember(strongModel, llm, "ComplianceOfficer",
                persona: "You are a compliance and regulatory expert. Evaluate proposals against applicable policies, regulations and legal frameworks. Flag any compliance gaps.")
            .AddMember(fastModel, llm, "Architect",
                persona: "You are a senior solution architect. Assess technical feasibility, dependencies, integration complexity and long-term maintainability.")
            .AddMember(fastModel, llm, "BusinessOwner",
                persona: "You are a pragmatic business owner. Focus on business value, time-to-market, cost and strategic alignment.")
            .SetChairman(Chairman.CreateStandard(strongModel, llm,
                systemPrompt: """
                    You are the Chair of the Risk Committee.
                    After the debate rounds, synthesise all arguments into a structured verdict.
                    Produce a JSON object matching the RiskVerdict schema:
                    {
                      "Recommendation": "Approve|ApproveWithConditions|Reject|Escalate",
                      "RiskLevel": "Low|Medium|High|Critical",
                      "Confidence": 0.0-1.0,
                      "Rationale": "<concise rationale>",
                      "Conditions": ["<condition>"],
                      "Risks": [
                        { "Category": "<cat>", "Description": "<desc>", "Severity": "Low|Medium|High|Critical", "Mitigation": "<mitigation>" }
                      ]
                    }
                    """)
            )
            .WithConsensusDebate()
            .WithVoting(new WeightedVotingStrategy(weights))
            .WithSystemPrompt(
                $"""You are a member of the Risk Committee evaluating the following proposal:\n\n{request.Question}\n\nContext:\n{request.InputData?.ToString() ?? "(no additional context provided)"}""")
            .WithUserPrompt(request.Question)
            .WithMaxRounds(maxRounds)
            .WithTemperature(temp)
            .WithStructuredOutput<RiskVerdict>();

        // Optionally attach compression
        var compressionStrategy = request.Options?.CompressionStrategy ?? "Hybrid";
        if (!string.Equals(compressionStrategy, "None", StringComparison.OrdinalIgnoreCase))
        {
            var embeddings = new Delibera.Core.Providers.RAG.OllamaEmbeddingProvider(
                (Delibera.Core.Providers.LLM.OllamaProvider)llm, embeddingModel);
            builder.WithCompression(
                Enum.Parse<Delibera.Core.Compression.CompressionStrategy>(compressionStrategy, true),
                llm, fastModel, embeddings);
        }

        return builder;
    }
}
