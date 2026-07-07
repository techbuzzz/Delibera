namespace Delibera.Server.Api.Contracts;

// ── Generic structured verdict (used as fallback / base) ──────────────────────

public sealed record VerdictDto
{
    public string?        Recommendation { get; init; }
    public string?        RiskLevel      { get; init; }   // Low | Medium | High | Critical
    public string?        Rationale      { get; init; }
    public float?         Confidence     { get; init; }   // 0.0 – 1.0
    public string[]?      Conditions     { get; init; }
    public RiskItemDto[]? Risks          { get; init; }
    public string?        RawJson        { get; init; }   // full chairman JSON when schema unknown
}

public sealed record RiskItemDto
{
    public required string Category    { get; init; }
    public required string Description { get; init; }
    public required string Severity    { get; init; }   // Low | Medium | High | Critical
    public          string? Mitigation { get; init; }
}

public sealed record VotingResultDto
{
    public required string   Strategy     { get; init; }
    public required string   Winner       { get; init; }
    public required int      TotalVotes   { get; init; }
    public required VoteTallyDto[] Tally  { get; init; }
}

public sealed record VoteTallyDto
{
    public required string Candidate { get; init; }
    public required int    Votes     { get; init; }
    public required float  Score     { get; init; }
}

public sealed record TokenStatsDto
{
    public required int TotalInputTokens  { get; init; }
    public required int TotalOutputTokens { get; init; }
    public required int TotalTokens       { get; init; }
    public required int SavedByCompression { get; init; }
}
