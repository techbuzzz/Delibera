namespace Delibera.Core.Models;

/// <summary>
/// Aggregated token usage statistics for an entire debate session.
/// Tracks original vs. compressed tokens across all rounds and Knowledge Keeper interactions.
/// </summary>
public sealed record TokenStatistics
{
   /// <summary>Total tokens in all original (uncompressed) prompts.</summary>
   public int TotalOriginalTokens { get; init; }

   /// <summary>Total tokens actually sent to LLMs after compression.</summary>
   public int TotalCompressedTokens { get; init; }

   /// <summary>Total tokens received in LLM responses.</summary>
   public int TotalResponseTokens { get; init; }

   /// <summary>Total tokens saved by compression.</summary>
   public int TokensSaved => TotalOriginalTokens - TotalCompressedTokens;

   /// <summary>Overall compression ratio (0.0–1.0; lower = more compression).</summary>
   public double OverallCompressionRatio => TotalOriginalTokens > 0
       ? (double)TotalCompressedTokens / TotalOriginalTokens
       : 1.0;

   /// <summary>Percentage of tokens saved across the entire debate.</summary>
   public double SavedPercent => (1.0 - OverallCompressionRatio) * 100.0;

   /// <summary>Grand total of all token usage (compressed prompts + responses).</summary>
   public int GrandTotal => TotalCompressedTokens + TotalResponseTokens;

   /// <summary>Per-round breakdown of token usage.</summary>
   public IReadOnlyList<RoundTokenUsage> RoundBreakdown { get; init; } = [];

   /// <summary>
   /// Formats the statistics as a human-readable summary.
   /// </summary>
   public string ToSummary()
   {
      var sb = new StringBuilder();
      sb.AppendLine("📊 Token Statistics:");
      sb.AppendLine($"  Original tokens:    {TotalOriginalTokens:N0}");
      sb.AppendLine($"  Compressed tokens:  {TotalCompressedTokens:N0}");
      sb.AppendLine($"  Response tokens:    {TotalResponseTokens:N0}");
      sb.AppendLine($"  Tokens saved:       {TokensSaved:N0} ({SavedPercent:F1}%)");
      sb.AppendLine($"  Compression ratio:  {OverallCompressionRatio:P1}");
      sb.AppendLine($"  Grand total:        {GrandTotal:N0}");
      return sb.ToString();
   }
}

/// <summary>
/// Token usage breakdown for a single debate round.
/// </summary>
/// <param name="RoundNumber">Round number (1-based).</param>
/// <param name="RoundName">Round display name.</param>
/// <param name="OriginalTokens">Original (uncompressed) prompt tokens for this round.</param>
/// <param name="CompressedTokens">Compressed prompt tokens for this round.</param>
/// <param name="ResponseTokens">Total response tokens from all members in this round.</param>
/// <param name="CompressionStrategy">Name of the compression strategy applied (or "None").</param>
public sealed record RoundTokenUsage(
    int RoundNumber,
    string RoundName,
    int OriginalTokens,
    int CompressedTokens,
    int ResponseTokens,
    string CompressionStrategy);
