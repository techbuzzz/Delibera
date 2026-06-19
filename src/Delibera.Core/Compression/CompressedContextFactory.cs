namespace Delibera.Core.Compression;

/// <summary>
///    Internal builder that produces <see cref="CompressedContext" /> values
///    with consistent defaults. Eliminates duplicated boilerplate across
///    every compressor implementation.
/// </summary>
internal static class CompressedContextFactory
{
   /// <summary>
   ///    Builds a result describing text that was passed through unchanged.
   /// </summary>
   public static CompressedContext PassThrough(string text, int tokens, string strategyName, TimeSpan duration)
   {
      return new CompressedContext
      {
         Text = text,
         OriginalLength = text.Length,
         CompressedLength = text.Length,
         OriginalTokens = tokens,
         CompressedTokens = tokens,
         StrategyUsed = strategyName,
         Duration = duration
      };
   }

   /// <summary>
   ///    Builds a result describing text that was actually compressed.
   /// </summary>
   public static CompressedContext Compressed(
      string original,
      string compressed,
      int originalTokens,
      int compressedTokens,
      string strategyName,
      TimeSpan duration)
   {
      return new CompressedContext
      {
         Text = compressed,
         OriginalLength = original.Length,
         CompressedLength = compressed.Length,
         OriginalTokens = originalTokens,
         CompressedTokens = compressedTokens,
         StrategyUsed = strategyName,
         Duration = duration
      };
   }
}