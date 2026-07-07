namespace Delibera.Server.Templates.Engineering;

/// <summary>Structured output schema for CodeReviewTemplate.</summary>
public sealed record CodeReviewVerdict
{
    public required string         Decision    { get; init; }   // Merge | RequestChanges | Escalate | Reject
    public required string         Summary     { get; init; }
    public required float          Confidence  { get; init; }   // 0.0 – 1.0
    public          ReviewIssue[]? Issues      { get; init; }
    public          string[]?      Suggestions { get; init; }
}

public sealed record ReviewIssue
{
    public required string  Severity    { get; init; }   // Blocker | Major | Minor | Nitpick
    public required string  Description { get; init; }
    public          string? Location    { get; init; }   // file:line
    public          string? Suggestion  { get; init; }
}

/// <summary>Structured output schema for RequirementsReviewTemplate.</summary>
public sealed record RequirementsVerdict
{
    public required string             Assessment      { get; init; }   // Approved | NeedsRevision | Rejected
    public required string             Summary         { get; init; }
    public required float              Confidence      { get; init; }   // 0.0 – 1.0
    public          RequirementGap[]?  Gaps            { get; init; }
    public          string[]?          Risks           { get; init; }
    public          string[]?          Recommendations { get; init; }
}

public sealed record RequirementGap
{
    public required string  Type         { get; init; }   // Missing | Ambiguous | Conflicting | OutOfScope
    public required string  Description  { get; init; }
    public          string? AffectedArea { get; init; }
    public          string? Suggestion   { get; init; }
}
