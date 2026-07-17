namespace Delibera.Server.Templates.Legal;

/// <summary>Structured output schema for LegalContractReviewTemplate.</summary>
public sealed record LegalContractVerdict
{
   public required string Recommendation { get; init; } // SignAsIs | SignWithRedlines | Negotiate | Reject
   public required string RiskLevel { get; init; } // Low | Medium | High | Critical
   public required float Confidence { get; init; } // 0.0 – 1.0
   public required string Summary { get; init; }
   public LegalIssue[]? Issues { get; init; }
   public string[]? PrivacyFlags { get; init; }
   public string[]? CommercialNotes { get; init; }
}

public sealed record LegalIssue
{
   public required string Severity { get; init; } // Blocker | Major | Minor
   public required string Description { get; init; }
   public string? ClauseRef { get; init; } // e.g. "§ 4.2" or "Clause 7"
   public string? Redline { get; init; } // suggested revised wording
}
