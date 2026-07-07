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
            DebateId     = record.DebateId,
            TemplateId   = record.TemplateId,
            TenantId     = record.TenantId,
            Status       = record.Status,
            FinalVerdict = record.Result?.FinalVerdict,
            Verdict      = record.Result?.MapVerdict(),
            Voting       = record.Result?.VotingTally?.MapVoting(),   // VotingTally, not VotingResult
            TokenStats   = record.Result?.TokenStats?.MapStats(),
            ErrorMessage = record.ErrorMessage,
            StreamUrl    = $"{baseUrl}/api/v1/debates/{record.DebateId}/stream",
            ResultUrl    = $"{baseUrl}/api/v1/debates/{record.DebateId}/result",
            CreatedAt    = record.CreatedAt,
            CompletedAt  = record.CompletedAt,
        };
    }

    private static VerdictDto? MapVerdict(this DebateResult result)
    {
        if (result.FinalVerdict is null) return null;
        return new VerdictDto
        {
            Recommendation = result.FinalVerdict,
            RawJson        = result.StructuredOutputJson,
        };
    }

    // Maps Delibera.Core.Voting.VotingResult → VotingResultDto
    // Core record:  WinningOption, Score, Scores (dict), Method
    // Server DTO:   Strategy,      Winner, TotalVotes,   Tally[]
    private static VotingResultDto MapVoting(this VotingResult v)
        => new()
        {
            Strategy   = v.Method,
            Winner     = v.WinningOption,
            TotalVotes = v.Scores.Count,
            Tally      = v.Scores
                .OrderByDescending(kv => kv.Value)
                .Select((kv, i) => new VoteTallyDto
                {
                    Candidate = kv.Key,
                    Votes     = i == 0 ? 1 : 0,          // plurality proxy — first entry = winner
                    Score     = (float)kv.Value,
                })
                .ToArray(),
        };

    private static TokenStatsDto? MapStats(this TokenStatistics s)
        => new()
        {
            TotalInputTokens   = s.TotalInputTokens,
            TotalOutputTokens  = s.TotalOutputTokens,
            TotalTokens        = s.TotalInputTokens + s.TotalOutputTokens,
            SavedByCompression = s.SavedByCompression,
        };

    public static DebateRoundDto ToDto(this DebateRound round)
        => new()
        {
            RoundNumber     = round.RoundNumber,
            IsFinal         = round.IsFinal,
            Strategy        = round.StrategyName ?? string.Empty,
            ChairmanSummary = round.ChairmanSummary,
            Messages        = round.Responses.Select(r => new ParticipantMessageDto
            {
                Role      = r.MemberName,
                Content   = r.Content,
                ModelName = r.ModelName,
            }).ToArray(),
            OperatorInteractions = round.OperatorInteractions?
                .Select(o => new OperatorInteractionDto { Task = o.Task, Result = o.Result })
                .ToArray(),
        };
}
