namespace Delibera.Server.Api.Contracts;

public sealed record DebateResponse
{
   public required string DebateId { get; init; }
   public required string TemplateId { get; init; }
   public required string TenantId { get; init; }
   public required DebateStatus Status { get; init; }
   public string? FinalVerdict { get; init; }
   public VerdictDto? Verdict { get; init; } // typed structured output
   public VotingResultDto? Voting { get; init; }
   public TokenStatsDto? TokenStats { get; init; }
   public string? ErrorMessage { get; init; }
   public bool? CacheHit { get; init; }
   public string? CacheKey { get; init; }
   public string? Label { get; init; }
   public required string StreamUrl { get; init; } // SSE endpoint
   public required string ResultUrl { get; init; }
   public required DateTimeOffset CreatedAt { get; init; }
   public DateTimeOffset? CompletedAt { get; init; }
}

public enum DebateStatus
{
   Pending,
   Running,
   Completed,
   Failed,
   Cancelled
}
