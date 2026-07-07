using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Templates.Governance;

/// <summary>
/// Architecture Decision Council — for ADR / RFC decisions.
///
/// Members:
///   Architect (lead)  — patterns, maintainability, coupling
///   SecurityEngineer  — threat model, attack surface
///   PerformanceEngineer — throughput, latency, scalability
///   TechLead          — team capability, delivery risk, pragmatism
///
/// Strategy : CritiqueDebate → BordaCountVoting → Chairman produces ADR summary.
/// </summary>
public sealed class ArchitectureDecisionTemplate : IServerTemplate
{
    public string   TemplateId       => "architecture-decision";
    public string   DisplayName      => "Architecture Decision Council";
    public string?  Description      => "Structured council for architecture decisions, RFCs and ADR reviews.";
    public string   Strategy         => "CritiqueDebate";
    public int      DefaultMaxRounds => 4;
    public string[] MemberRoles      => ["Architect", "SecurityEngineer", "PerformanceEngineer", "TechLead"];
    public string   VotingStrategy   => "BordaCount";
    public bool     RagEnabled       => true;
    public bool     OperatorEnabled  => false;

    public ICouncilBuilder Configure(
        CreateDebateRequest request,
        IServiceProvider    services,
        IConfiguration      configuration)
    {
        var endpoint    = configuration["Delibera:Providers:DefaultEndpoint"] ?? "http://localhost:11434";
        var apiKey      = configuration["Delibera:Providers:ApiKey"];
        var factory     = new ProviderFactory();
        var llm         = string.IsNullOrEmpty(apiKey)
            ? factory.CreateOllama(endpoint)
            : factory.CreateCloudOllama(endpoint,apiKey);

        var fastModel   = configuration["Delibera:Models:Fast"]   ?? "llama3.2:3b";
        var strongModel = configuration["Delibera:Models:Strong"] ?? "qwen2.5:7b";
        var maxRounds   = request.Options?.MaxRounds ?? DefaultMaxRounds;

        return new CouncilBuilder()
            .AddMember(strongModel, llm, "Architect",
                persona: "Senior solution architect. Champion of clean boundaries, SOLID, and long-term maintainability.")
            .AddMember(fastModel, llm, "SecurityEngineer",
                persona: "Application security expert. Evaluates threat surface, trust boundaries, secrets management and compliance.")
            .AddMember(fastModel, llm, "PerformanceEngineer",
                persona: "Performance and reliability engineer. Focuses on latency, throughput, scalability and failure modes.")
            .AddMember(fastModel, llm, "TechLead",
                persona: "Pragmatic tech lead. Weighs team skills, delivery risk, operational complexity and time-to-market.")
            .SetChairman(Chairman.CreateCustom(strongModel, llm,
                """
                    You are the Architecture Decision Chair.
                    After debate, produce an ADR-style JSON verdict:
                    {
                      "Decision": "Adopt|Reject|Defer|Spike",
                      "Rationale": "<concise>",
                      "Consequences": ["<positive/negative consequence>"],
                      "Alternatives": ["<rejected alternative>"],
                      "Risks": [{ "Category": "<cat>", "Description": "<desc>", "Severity": "Low|Medium|High|Critical" }]
                    }
                    """)
            )
            .WithCritiqueDebate()
            .WithVoting(new BordaCountVotingStrategy())
            .WithSystemPrompt(
                $"Architecture Decision Council\n\nQuestion:\n{request.Question}\n\nContext:\n{request.InputData?.ToString() ?? string.Empty}")
            .WithUserPrompt(request.Question)
            .WithMaxRounds(maxRounds)
            .WithTemperature(request.Options?.Temperature ?? 0.7f)
            .WithStructuredOutput<ArchitectureVerdict>();
    }
}
