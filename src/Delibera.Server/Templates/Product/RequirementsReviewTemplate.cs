using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Engineering;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Templates.Product;

/// <summary>
/// Requirements Review Council — Vertical 3: Requirements Engineering &amp; Product Discovery.
///
/// Members:
///   ProductManager  — user value, business goals, prioritisation
///   Architect       — technical feasibility, constraints, dependencies
///   UXDesigner      — usability, accessibility, user journey
///   QA              — testability, acceptance criteria, edge cases
///   SecurityOfficer — data privacy, compliance, threat model
///
/// Strategy : ConsensusDebate → MajorityVoting → Chairman produces RequirementsVerdict.
/// Output   : RequirementsVerdict (Assessment, Summary, Gaps[], Risks[], Recommendations[])
/// </summary>
public sealed class RequirementsReviewTemplate : IServerTemplate
{
    public string   TemplateId       => "requirements-review";
    public string   DisplayName      => "Requirements Review Council";
    public string?  Description      => "AI council for validating and improving product requirements, user stories and acceptance criteria.";
    public string   Strategy         => "ConsensusDebate";
    public int      DefaultMaxRounds => 4;
    public string[] MemberRoles      => ["ProductManager", "Architect", "UXDesigner", "QA", "SecurityOfficer"];
    public string   VotingStrategy   => "Majority";
    public bool     RagEnabled       => true;
    public bool     OperatorEnabled  => false;

    public CouncilBuilder Configure(
        CreateDebateRequest request,
        IServiceProvider    services,
        IConfiguration      configuration)
    {
        var endpoint    = configuration["Delibera:Providers:DefaultEndpoint"] ?? "http://localhost:11434";
        var apiKey      = configuration["Delibera:Providers:ApiKey"];
        var factory     = new ProviderFactory();
        var llm         = string.IsNullOrEmpty(apiKey)
            ? factory.CreateOllama(endpoint)
            : factory.CreateOllamaCloud(apiKey);

        var fastModel   = configuration["Delibera:Models:Fast"]   ?? "llama3.2:3b";
        var strongModel = configuration["Delibera:Models:Strong"] ?? "qwen2.5:7b";
        var maxRounds   = request.Options?.MaxRounds ?? DefaultMaxRounds;
        var temp        = request.Options?.Temperature ?? 0.6f;

        return new CouncilBuilder()
            .AddMember(strongModel, llm, "ProductManager",
                persona: "Experienced product manager. Champion of user value and business outcomes. " +
                         "Identify missing acceptance criteria, unclear scope and priority conflicts. " +
                         "Think in terms of Jobs-to-be-Done and measurable outcomes.")
            .AddMember(strongModel, llm, "Architect",
                persona: "Senior solution architect. Assess technical feasibility, identify hidden dependencies, " +
                         "integration complexity and non-functional requirements (performance, scalability, resilience).")
            .AddMember(fastModel, llm, "UXDesigner",
                persona: "UX and accessibility specialist. Evaluate whether requirements address real user journeys, " +
                         "flag usability risks and missing accessibility considerations.")
            .AddMember(fastModel, llm, "QA",
                persona: "Quality assurance lead. Focus on testability: are requirements verifiable? " +
                         "Identify missing edge cases, ambiguous acceptance criteria and regression risks.")
            .AddMember(fastModel, llm, "SecurityOfficer",
                persona: "Security and compliance officer. Evaluate data privacy implications, GDPR/CCPA compliance, " +
                         "authentication/authorisation requirements and threat model gaps.")
            .SetChairman(Chairman.CreateStandard(strongModel, llm,
                systemPrompt: """
                    You are the Requirements Review Chair.
                    After the debate, produce a structured JSON verdict:
                    {
                      "Assessment": "Approved|NeedsRevision|Rejected",
                      "Summary": "<one-paragraph synthesis>",
                      "Confidence": 0.0-1.0,
                      "Gaps": [
                        { "Type": "Missing|Ambiguous|Conflicting|OutOfScope", "Description": "<desc>", "AffectedArea": "<area>", "Suggestion": "<fix>" }
                      ],
                      "Risks": ["<risk statement>"],
                      "Recommendations": ["<actionable recommendation>"]
                    }
                    """)
            )
            .WithConsensusDebate()
            .WithVoting(new MajorityVotingStrategy())
            .WithSystemPrompt(
                $"""Requirements Review Council\n\nRequirements / User Story:\n{request.Question}\n\nAdditional Context:\n{request.InputData?.ToString() ?? "(no additional context)"}""")
            .WithUserPrompt(request.Question)
            .WithMaxRounds(maxRounds)
            .WithTemperature(temp)
            .WithStructuredOutput<RequirementsVerdict>();
    }
}
