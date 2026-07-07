using Delibera.Core.Models;
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
            Voting       = record.Result?.VotingResult?.MapVoting(),
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

    private static VotingResultDto? MapVoting(this VotingResult v)
        => new()
        {
            Strategy   = v.StrategyName,
            Winner     = v.Winner,
            TotalVotes = v.TotalVotes,
            Tally      = v.Tally.Select(t => new VoteTallyDto
            {
                Candidate = t.Candidate,
                Votes     = t.Votes,
                Score     = t.Score,
            }).ToArray(),
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
            RoundNumber   = round.RoundNumber,
            IsFinal       = round.IsFinal,
            Strategy      = round.StrategyName ?? string.Empty,
            ChairmanSummary = round.ChairmanSummary,
            Messages      = round.Responses.Select(r => new ParticipantMessageDto
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
