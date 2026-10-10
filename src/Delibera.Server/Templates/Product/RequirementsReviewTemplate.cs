using Delibera.Core;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Voting;
using Delibera.Server.Templates.Engineering;

namespace Delibera.Server.Templates.Product;

/// <summary>
///    Requirements Review Council â€” Vertical 3: Requirements Engineering &amp; Product Discovery.
///    Members:
///    ProductManager  â€” user value, business goals, prioritisation
///    Architect       â€” technical feasibility, constraints, dependencies
///    UXDesigner      â€” usability, accessibility, user journey
///    QA              â€” testability, acceptance criteria, edge cases
///    SecurityOfficer â€” data privacy, compliance, threat model
///    Strategy : ConsensusDebate â†’ MajorityVoting â†’ Chairman produces RequirementsVerdict.
///    Output   : RequirementsVerdict (Assessment, Summary, Gaps[], Risks[], Recommendations[])
/// </summary>
public sealed class RequirementsReviewTemplate : IServerTemplate
{
   public string TemplateId => "requirements-review";
   public string DisplayName => "Requirements Review Council";

   public string? Description =>
      "AI council for validating and improving product requirements, user stories and acceptance criteria.";

   public string Strategy => "ConsensusDebate";
   public int DefaultMaxRounds => 4;
   public string[] MemberRoles => ["ProductManager", "Architect", "UXDesigner", "QA", "SecurityOfficer"];
   public string VotingStrategy => "Majority";
   public bool RagEnabled => true;
   public bool OperatorEnabled => false;

   public ICouncilBuilder Configure(
      CreateDebateRequest request,
      IServiceProvider services,
      IConfiguration configuration)
   {
       var endpoint = configuration[BuiltIn.ConfigKeys.ProvidersDefaultEndpoint] ?? BuiltIn.Endpoints.OllamaLocal;
       var apiKey = configuration[BuiltIn.ConfigKeys.ProvidersApiKey];
       var factory = new ProviderFactory();
       var llm = string.IsNullOrEmpty(apiKey)
          ? factory.CreateOllama(endpoint)
          : factory.CreateCloudOllama(endpoint, apiKey);

       var fastModel = configuration[BuiltIn.ConfigKeys.ModelsFast] ?? BuiltIn.Models.DefaultFast;
       var strongModel = configuration[BuiltIn.ConfigKeys.ModelsStrong] ?? BuiltIn.Models.DefaultStrong;
      var maxRounds = request.Options?.MaxRounds ?? DefaultMaxRounds;
      var temp = request.Options?.Temperature ?? 0.6f;

      var builder = new CouncilBuilder()
         .AddMember(strongModel, llm, "ProductManager",
            "Experienced product manager. Champion of user value and business outcomes. " +
            "Identify missing acceptance criteria, unclear scope and priority conflicts. " +
            "Think in terms of Jobs-to-be-Done and measurable outcomes.")
         .AddMember(strongModel, llm, "Architect",
            "Senior solution architect. Assess technical feasibility, identify hidden dependencies, " +
            "integration complexity and non-functional requirements (performance, scalability, resilience).")
         .AddMember(fastModel, llm, "UXDesigner",
            "UX and accessibility specialist. Evaluate whether requirements address real user journeys, " +
            "flag usability risks and missing accessibility considerations.")
         .AddMember(fastModel, llm, "QA",
            "Quality assurance lead. Focus on testability: are requirements verifiable? " +
            "Identify missing edge cases, ambiguous acceptance criteria and regression risks.")
         .AddMember(fastModel, llm, "SecurityOfficer",
            "Security and compliance officer. Evaluate data privacy implications, GDPR/CCPA compliance, " +
            "authentication/authorisation requirements and threat model gaps.")
         .SetChairman(Chairman.CreateCustom(strongModel, llm,
            """
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

      return TemplateKnowledge.Attach(builder, request, services);
   }
}
