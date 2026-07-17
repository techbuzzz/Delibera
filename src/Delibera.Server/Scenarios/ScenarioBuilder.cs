using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Scenarios;

public static class ScenarioBuilder
{
    public static ICouncilBuilder Build(
        ScenarioRequest request,
        IConfiguration configuration)
   {
      if (request.Members is not { Length: > 0 })
         throw new ArgumentException("A scenario must have at least one member.", nameof(request));

      var endpoint = configuration["Delibera:Providers:DefaultEndpoint"] ?? "http://localhost:11434";
      var apiKey = configuration["Delibera:Providers:ApiKey"];
      var fastModel = configuration["Delibera:Models:Fast"] ?? "llama3.2:3b";
      var strongModel = configuration["Delibera:Models:Strong"] ?? "qwen2.5:7b";
       using var factory = new ProviderFactory();

      ILLMProvider DefaultProvider() =>
          string.IsNullOrEmpty(apiKey)
              ? factory.CreateLocalOllama(endpoint)
              : factory.CreateCloudOllama(endpoint, apiKey);

       ILLMProvider ResolveProvider(string? providerType) =>
           providerType?.ToLowerInvariant() switch
           {
              _ => DefaultProvider(),
           };

      string ResolveModel(string? model, bool preferStrong = true) =>
          !string.IsNullOrWhiteSpace(model) ? model
              : preferStrong ? strongModel : fastModel;

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

      foreach (var m in request.Members)
      {
         var model = ResolveModel(m.Model);
         var provider = ResolveProvider(m.Provider);
         var caps = m.Capabilities?.ToLowerInvariant() switch
         {
            "vision" => MemberCapabilities.Vision,
            "both" => MemberCapabilities.Text | MemberCapabilities.Vision,
            _ => MemberCapabilities.Text,
         };
         builder.AddMember(model, provider, m.Role, caps, m.Persona);
      }

      _ = request.Strategy?.ToLowerInvariant() switch
      {
         "critique" => builder.WithStrategy(new Delibera.Core.Debate.CritiqueDebate()),
         "consensus" => builder.WithStrategy(new Delibera.Core.Debate.ConsensusDebate()),
         _ => builder.WithStrategy(new Delibera.Core.Debate.StandardDebate()),
      };

      // ── Chairman ──────────────────────────────────────────────────────────
      if (request.Chairman is { } ch)
      {
         var chModel = ResolveModel(ch.Model, preferStrong: true);
         var chProvider = ResolveProvider(ch.Provider);
         // CreateWithOpening не существует — используем CreateStandard
         var chairman = Chairman.CreateStandard(chModel, chProvider);
         builder.SetChairman(chairman);
      }

      // ── Voting ────────────────────────────────────────────────────────────
      if (!string.IsNullOrWhiteSpace(request.VotingStrategy))
      {
         var chModel = strongModel;
         var chProvider = DefaultProvider();

          IVotingStrategy voting = request.VotingStrategy.ToLowerInvariant() switch
          {
             "bordacount" or "borda" => new BordaCountVotingStrategy(),
             "weighted" =>
                 new WeightedVotingStrategy
                 {
                     MemberWeights = request.MemberWeights is { Count: > 0 }
                         ? request.MemberWeights.ToDictionary(kv => kv.Key, kv => (double)kv.Value)
                         : request.Members.ToDictionary(m => m.Role, m => (double)m.Weight),
                 },
             _ => new MajorityVotingStrategy(),
          };
         builder.WithVotingChairman(chModel, chProvider, voting);
      }

      // ── Knowledge text (inline — добавляем в системный промпт) ───────────
      if (!string.IsNullOrWhiteSpace(request.KnowledgeText))
      {
         var existing = builder.GetSystemPrompt(); // если метод есть, иначе — пересобрать
                                                   // Т.к. WithKnowledgeText нет в ICouncilBuilder, инжектируем в system prompt:
         builder.WithSystemPrompt(
             (request.SystemPrompt ?? request.Question)
             + $"\n\n## Knowledge Context\n{request.KnowledgeText}");
      }

      // ── Compression ───────────────────────────────────────────────────────
      if (!string.IsNullOrEmpty(request.CompressionStrategy) &&
          !string.Equals(request.CompressionStrategy, "None", StringComparison.OrdinalIgnoreCase))
      {
         var llm = DefaultProvider();
          if (Enum.TryParse<CompressionStrategy>(
                  request.CompressionStrategy, true, out var cs))
            builder.WithCompression(cs, llm, strongModel);
      }

      return builder;
   }
}
