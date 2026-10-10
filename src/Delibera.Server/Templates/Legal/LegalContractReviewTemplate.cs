using Delibera.Core;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Voting;

namespace Delibera.Server.Templates.Legal;

/// <summary>
///    Legal Contract Review Council â€” Vertical 4: Legal / Policy / Compliance Analysis.
///    Members:
///    ContractLawyer   (weight 2.0) â€” clause-by-clause legal risk analysis
///    BusinessCounsel  (weight 2.0) â€” commercial terms, liability, SLA
///    PrivacyOfficer   (weight 1.5) â€” GDPR/CCPA, data processing, DPA clauses
///    RiskManager      (weight 1.0) â€” operational and financial risk
///    Strategy : CritiqueDebate â†’ WeightedVoting â†’ Chairman produces LegalContractVerdict.
///    Output   : LegalContractVerdict (Recommendation, RiskLevel, Issues[], PrivacyFlags[], Confidence)
/// </summary>
public sealed class LegalContractReviewTemplate : IServerTemplate
{
   public string TemplateId => "legal-contract-review";
   public string DisplayName => "Legal Contract Review Council";

   public string? Description =>
      "AI council for contract and policy analysis: risk identification, clause review and compliance check.";

   public string Strategy => "CritiqueDebate";
   public int DefaultMaxRounds => 4;
   public string[] MemberRoles => ["ContractLawyer", "BusinessCounsel", "PrivacyOfficer", "RiskManager"];
   public string VotingStrategy => "Weighted";
   public bool RagEnabled => true;
   public bool OperatorEnabled => false;

   public ICouncilBuilder Configure(
      CreateDebateRequest request,
      IServiceProvider services,
      IConfiguration configuration)
   {
       var endpoint = configuration[BuiltIn.ConfigKeys.ProvidersDefaultEndpoint] ?? BuiltIn.Endpoints.OllamaLocal;
       var apiKey = configuration[BuiltIn.ConfigKeys.ProvidersApiKey];
       using var factory = new ProviderFactory();
       var llm = string.IsNullOrEmpty(apiKey)
          ? factory.CreateOllama(endpoint)
          : factory.CreateCloudOllama(endpoint, apiKey);

       var fastModel = configuration[BuiltIn.ConfigKeys.ModelsFast] ?? BuiltIn.Models.DefaultFast;
       var strongModel = configuration[BuiltIn.ConfigKeys.ModelsStrong] ?? BuiltIn.Models.DefaultStrong;
      var maxRounds = request.Options?.MaxRounds ?? DefaultMaxRounds;
      var temp = request.Options?.Temperature ?? 0.3f;

      var weights = request.Options?.MemberWeights ??
                    new Dictionary<string, float>
                    {
                       ["ContractLawyer"] = 2.0f,
                       ["BusinessCounsel"] = 2.0f,
                       ["PrivacyOfficer"] = 1.5f,
                       ["RiskManager"] = 1.0f
                    };

      var builder = new CouncilBuilder()
         .AddMember(strongModel, llm, "ContractLawyer",
            "Experienced contract lawyer specialising in commercial and technology law. " +
            "Analyse each clause for enforceability, ambiguity, unfair terms and legal risk. " +
            "Reference relevant jurisdiction and applicable law where possible.")
         .AddMember(strongModel, llm, "BusinessCounsel",
            "Commercial legal counsel. Focus on liability caps, indemnification, IP ownership, " +
            "SLA obligations, termination rights and commercial risk balance.")
         .AddMember(fastModel, llm, "PrivacyOfficer",
            "Data privacy and compliance officer. Evaluate GDPR, CCPA and local data protection law compliance. " +
            "Identify missing DPA clauses, sub-processor controls and data retention obligations.")
         .AddMember(fastModel, llm, "RiskManager",
            "Operational and financial risk manager. Identify contractual obligations that create " +
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
         .WithVoting(new WeightedVotingStrategy
            { MemberWeights = weights.ToDictionary(kv => kv.Key, kv => (double)kv.Value) })
         .WithSystemPrompt(
            $"""Legal Contract Review Council\n\nContract / Clause to Review:\n{request.Question}\n\nContract Text:\n{request.InputData?.ToString() ?? "(no contract text provided)"}""")
         .WithUserPrompt(request.Question)
         .WithMaxRounds(maxRounds)
         .WithTemperature(temp)
         .WithStructuredOutput<LegalContractVerdict>();

      return TemplateKnowledge.Attach(builder, request, services);
   }
}
