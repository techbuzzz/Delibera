using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Templates.Legal;

/// <summary>
/// Legal Contract Review Council — Vertical 4: Legal / Policy / Compliance Analysis.
///
/// Members:
///   ContractLawyer   (weight 2.0) — clause-by-clause legal risk analysis
///   BusinessCounsel  (weight 2.0) — commercial terms, liability, SLA
///   PrivacyOfficer   (weight 1.5) — GDPR/CCPA, data processing, DPA clauses
///   RiskManager      (weight 1.0) — operational and financial risk
///
/// Strategy : CritiqueDebate → WeightedVoting → Chairman produces LegalContractVerdict.
/// Output   : LegalContractVerdict (Recommendation, RiskLevel, Issues[], PrivacyFlags[], Confidence)
/// </summary>
public sealed class LegalContractReviewTemplate : IServerTemplate
{
    public string   TemplateId       => "legal-contract-review";
    public string   DisplayName      => "Legal Contract Review Council";
    public string?  Description      => "AI council for contract and policy analysis: risk identification, clause review and compliance check.";
    public string   Strategy         => "CritiqueDebate";
    public int      DefaultMaxRounds => 4;
    public string[] MemberRoles      => ["ContractLawyer", "BusinessCounsel", "PrivacyOfficer", "RiskManager"];
    public string   VotingStrategy   => "Weighted";
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
            : factory.CreateCloudOllama(endpoint, apiKey);

        var fastModel   = configuration["Delibera:Models:Fast"]   ?? "llama3.2:3b";
        var strongModel = configuration["Delibera:Models:Strong"] ?? "qwen2.5:7b";
        var maxRounds   = request.Options?.MaxRounds ?? DefaultMaxRounds;
        var temp        = request.Options?.Temperature ?? 0.3f;

        var weights = request.Options?.MemberWeights ?? new Dictionary<string, float>
        {
            ["ContractLawyer"]  = 2.0f,
            ["BusinessCounsel"] = 2.0f,
            ["PrivacyOfficer"]  = 1.5f,
            ["RiskManager"]     = 1.0f,
        };

        return new CouncilBuilder()
            .AddMember(strongModel, llm, "ContractLawyer",
                persona: "Experienced contract lawyer specialising in commercial and technology law. " +
                         "Analyse each clause for enforceability, ambiguity, unfair terms and legal risk. " +
                         "Reference relevant jurisdiction and applicable law where possible.")
            .AddMember(strongModel, llm, "BusinessCounsel",
                persona: "Commercial legal counsel. Focus on liability caps, indemnification, IP ownership, " +
                         "SLA obligations, termination rights and commercial risk balance.")
            .AddMember(fastModel, llm, "PrivacyOfficer",
                persona: "Data privacy and compliance officer. Evaluate GDPR, CCPA and local data protection law compliance. " +
                         "Identify missing DPA clauses, sub-processor controls and data retention obligations.")
            .AddMember(fastModel, llm, "RiskManager",
                persona: "Operational and financial risk manager. Identify contractual obligations that create " +
                         "financial exposure, operational dependencies or reputational risk.")
            .SetChairman(Chairman.CreateCustom(strongModel, llm,
                """
                    You are the Legal Review Chair.
                    After the debate, produce a structured JSON verdict:
                    {
                      "Recommendation": "SignAsIs|SignWithRedlines|Negotiate|Reject",
                      "RiskLevel": "Low|Medium|High|Critical",
                      "Confidence": 0.0-1.0,
                      "Summary": "<concise overall assessment>",
                      "Issues": [
                        { "Severity": "Blocker|Major|Minor", "ClauseRef": "<section/clause>", "Description": "<issue>", "Redline": "<suggested revision>" }
                      ],
                      "PrivacyFlags": ["<GDPR/CCPA gap or concern>"],
                      "CommercialNotes": ["<commercial observation>"]
                    }
                    """)
            )
            .WithCritiqueDebate()
            .WithVoting(new WeightedVotingStrategy { MemberWeights = weights.ToDictionary(kv => kv.Key, kv => (double)kv.Value) })
            .WithSystemPrompt(
                $"""Legal Contract Review Council\n\nContract / Clause to Review:\n{request.Question}\n\nContract Text:\n{request.InputData?.ToString() ?? "(no contract text provided)"}""")
            .WithUserPrompt(request.Question)
            .WithMaxRounds(maxRounds)
            .WithTemperature(temp)
            .WithStructuredOutput<LegalContractVerdict>();
    }
}
