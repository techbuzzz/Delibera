namespace Delibera.Server.Templates.Governance;

/// <summary>Structured output schema for RiskCommitteeTemplate.</summary>
public sealed record RiskVerdict
{
    public required string     Recommendation { get; init; }  // Approve | ApproveWithConditions | Reject | Escalate
    public required string     RiskLevel      { get; init; }  // Low | Medium | High | Critical
    public required float      Confidence     { get; init; }  // 0.0 – 1.0
    public required string     Rationale      { get; init; }
    public          string[]?  Conditions     { get; init; }
    public          RiskEntry[]? Risks        { get; init; }
}

public sealed record RiskEntry
{
    public required string  Category    { get; init; }
    public required string  Description { get; init; }
    public required string  Severity    { get; init; }  // Low | Medium | High | Critical
    public          string? Mitigation  { get; init; }
}

/// <summary>Structured output schema for ArchitectureDecisionTemplate.</summary>
public sealed record ArchitectureVerdict
{
    public required string    Decision     { get; init; }  // Adopt | Reject | Defer | Spike
    public required string    Rationale    { get; init; }
    public          string[]? Consequences { get; init; }
    public          string[]? Alternatives { get; init; }
    public          RiskEntry[]? Risks     { get; init; }
}
