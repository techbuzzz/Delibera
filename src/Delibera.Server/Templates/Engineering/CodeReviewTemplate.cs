using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Templates.Engineering;

/// <summary>
/// Code Review Council — Vertical 2: Software Engineering &amp; Code/Architecture Review.
///
/// Members:
///   Reviewer         — code quality, patterns, readability
///   Defender         — represents the author, explains intent
///   QA               — edge cases, testability, regression risk
///   SecurityEngineer — vulnerability, injection, secrets exposure
///   TechLead         — overall judgement: merge / request changes / escalate
///
/// Strategy : CritiqueDebate → BordaCountVoting → Chairman produces structured verdict.
/// Output   : CodeReviewVerdict (Decision, Summary, Issues[], Suggestions[], Confidence)
/// </summary>
public sealed class CodeReviewTemplate : IServerTemplate
{
   public string TemplateId => "code-review";
   public string DisplayName => "Code Review Council";
   public string? Description => "Multi-role AI council for pull-request and code review with structured verdict.";
   public string Strategy => "CritiqueDebate";
   public int DefaultMaxRounds => 4;
   public string[] MemberRoles => ["Reviewer", "Defender", "QA", "SecurityEngineer", "TechLead"];
   public string VotingStrategy => "BordaCount";
   public bool RagEnabled => true;
   public bool OperatorEnabled => true;

   public ICouncilBuilder Configure(
      CreateDebateRequest request,
      IServiceProvider services,
      IConfiguration configuration)
   {
      var endpoint = configuration["Delibera:Providers:DefaultEndpoint"] ?? "http://localhost:11434";
      var apiKey = configuration["Delibera:Providers:ApiKey"];
      using var factory = new ProviderFactory();
      var llm = string.IsNullOrEmpty(apiKey)
         ? factory.CreateOllama(endpoint)
         : factory.CreateCloudOllama(endpoint, apiKey);

      var fastModel = configuration["Delibera:Models:Fast"] ?? "llama3.2:3b";
      var strongModel = configuration["Delibera:Models:Strong"] ?? "qwen2.5:7b";
      var maxRounds = request.Options?.MaxRounds ?? DefaultMaxRounds;
      var temp = request.Options?.Temperature ?? 0.4f;

      return new CouncilBuilder()
         .AddMember(strongModel, llm, "Reviewer",
            persona: "Senior code reviewer. Champion of clean code, SOLID principles, naming clarity, and maintainability. " +
                     "You cite specific line numbers and patterns. You are thorough and objective.")
         .AddMember(fastModel, llm, "Defender",
            persona: "You represent the author of the code change. Explain the intent and trade-offs of the implementation. " +
                     "Defend reasonable decisions and concede where criticism is valid.")
         .AddMember(fastModel, llm, "QA",
            persona: "Quality assurance and testing expert. Focus on testability, edge cases, error handling, " +
                     "regression risk and missing test coverage.")
         .AddMember(fastModel, llm, "SecurityEngineer",
            persona: "Application security engineer. Identify injection risks, broken auth, secrets exposure, " +
                     "dependency vulnerabilities and insecure defaults.")
         .AddMember(strongModel, llm, "TechLead",
            persona: "Experienced tech lead. Balance quality bar with delivery pragmatism. " +
                     "Make the final call: merge as-is, request changes, or escalate to architecture review.")
         .SetChairman(Chairman.CreateCustom(strongModel, llm,
            """
            You are the Code Review Chair.
            After the debate, produce a structured JSON verdict:
            {
              "Decision": "Merge|RequestChanges|Escalate|Reject",
              "Summary": "<one-paragraph summary>",
              "Confidence": 0.0-1.0,
              "Issues": [
                { "Severity": "Blocker|Major|Minor|Nitpick", "Location": "<file:line>", "Description": "<desc>", "Suggestion": "<fix>" }
              ],
              "Suggestions": ["<non-blocking improvement>"]
            }
            """)
         )
         .WithCritiqueDebate()
         .WithVoting(new BordaCountVotingStrategy())
         .WithSystemPrompt(
            $"""Code Review Council\n\nChange Description:\n{request.Question}\n\nDiff / Context:\n{request.InputData?.ToString() ?? "(no diff provided)"}""")
         .WithUserPrompt(request.Question)
         .WithMaxRounds(maxRounds)
         .WithTemperature(temp)
         .WithStructuredOutput<CodeReviewVerdict>();
   }
}
