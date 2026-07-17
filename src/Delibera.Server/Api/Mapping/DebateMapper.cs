using Delibera.Core.Models;
using Delibera.Core.Voting;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;

namespace Delibera.Server.Api.Mapping;

public static class DebateMapper
{
   public static DebateResponse ToResponse(this DebateRecord record, HttpContext ctx)
   {
      var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
      return new DebateResponse
      {
         DebateId = record.DebateId,
         TemplateId = record.TemplateId,
         TenantId = record.TenantId,
         Status = record.Status,
         FinalVerdict = record.Result?.FinalVerdict,
         Verdict = record.Result?.MapVerdict(),
         Voting = record.Result?.VotingTally?.MapVoting(),
         TokenStats = record.Result?.TokenStats?.MapStats(),
         ErrorMessage = record.ErrorMessage,
         CacheHit = record.Result?.CacheHit,
         CacheKey = record.Result?.CacheKey,
         Label = record.Label,
         StreamUrl = $"{baseUrl}/api/v1/debates/{record.DebateId}/stream",
         ResultUrl = $"{baseUrl}/api/v1/debates/{record.DebateId}/result",
         CreatedAt = record.CreatedAt,
         CompletedAt = record.CompletedAt,
      };
   }

   private static VerdictDto? MapVerdict(this DebateResult result)
   {
      if (result.FinalVerdict is null) return null;
      return new VerdictDto
      {
         Recommendation = result.FinalVerdict,
         RawJson = result.TypedVerdict is not null
            ? System.Text.Json.JsonSerializer.Serialize(result.TypedVerdict)
            : null,
      };
   }

   private static VotingResultDto MapVoting(this VotingResult v)
      => new()
      {
         Strategy = v.Method,
         Winner = v.WinningOption,
         TotalVotes = v.Scores.Count,
         Tally = v.Scores
            .OrderByDescending(kv => kv.Value)
            .Select((kv, i) => new VoteTallyDto
            {
               Candidate = kv.Key,
               Votes = i == 0
                  ? 1
                  : 0,
               Score = (float)kv.Value,
            })
            .ToArray(),
      };

   private static TokenStatsDto? MapStats(this TokenStatistics s)
      => new()
      {
         TotalInputTokens = s.TotalOriginalTokens,
         TotalOutputTokens = s.TotalResponseTokens,
         TotalTokens = s.GrandTotal,
         SavedByCompression = s.TokensSaved,
      };

   public static DebateRoundDto ToDto(this DebateRound round)
      => new()
      {
         RoundNumber = round.RoundNumber,
         IsFinal = round.IsFinal,
         Strategy = round.StrategyUsed?.GetType().Name ?? string.Empty,
         ChairmanSummary = round.Responses.TryGetValue("Chairman", out var summary)
            ? summary
            : null,
         Messages = round.Responses
            .Where(r => r.Key != "Chairman")
            .Select(r => new ParticipantMessageDto
            {
               Role = r.Key,
               Content = r.Value,
               ModelName = string.Empty,
            }).ToArray(),
         OperatorInteractions = round.OperatorInteractions?
            .Select(o => new OperatorInteractionDto { Task = o.Task, Result = o.Answer })
            .ToArray(),
      };
}
