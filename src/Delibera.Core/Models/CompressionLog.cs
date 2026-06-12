namespace Delibera.Core.Models;

/// <summary>
///    Log entry recording a single context compression operation during a debate.
/// </summary>
public sealed record CompressionLog
{
   /// <summary>When the compression occurred.</summary>
   public DateTime Timestamp { get; init; } = DateTime.UtcNow;

   /// <summary>Round number during which the compression occurred (0 = pre-debate).</summary>
   public int RoundNumber { get; init; }

   /// <summary>Description of what was compressed (e.g., "Round 2 prompt", "Knowledge context").</summary>
   public required string Description { get; init; }

   /// <summary>Compression strategy name.</summary>
   public required string StrategyName { get; init; }

   /// <summary>Original token count before compression.</summary>
   public int OriginalTokens { get; init; }

   /// <summary>Token count after compression.</summary>
   public int CompressedTokens { get; init; }

   /// <summary>Tokens saved by this operation.</summary>
   public int TokensSaved => OriginalTokens - CompressedTokens;

   /// <summary>Compression ratio for this operation.</summary>
   public double Ratio => OriginalTokens > 0
      ? (double)CompressedTokens / OriginalTokens
      : 1.0;

   /// <summary>Duration of the compression operation.</summary>
   public TimeSpan Duration { get; init; }
}