namespace Delibera.Core.Providers.RAG;

/// <summary>
///    Shared text-chunking logic for RAG providers.
///    Produces overlapping chunks, breaking on paragraph or sentence boundaries where possible.
/// </summary>
internal static class TextChunker
{
   private static readonly string[] Separators = ["\n\n", "\r\n\r\n", "\n", ". ", "! ", "? "];

   /// <summary>
   ///    Splits text into overlapping chunks of approximately <paramref name="chunkSize" /> characters.
   /// </summary>
   public static List<string> SplitIntoChunks(string text, int chunkSize, int chunkOverlap)
   {
      if (string.IsNullOrWhiteSpace(text)) return [];
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
      ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlap);
      if (chunkOverlap >= chunkSize)
         throw new ArgumentException("chunkOverlap must be smaller than chunkSize.", nameof(chunkOverlap));

      var chunks = new List<string>();
      var pos = 0;

      while (pos < text.Length)
      {
         var end = Math.Min(pos + chunkSize, text.Length);

         if (end < text.Length)
         {
            var minBreakPos = pos + Math.Max(1, chunkSize / 2);
            var bestBreak = -1;
            foreach (var sep in Separators)
            {
               var searchStart = Math.Min(end, text.Length);
               var idx = text.LastIndexOf(sep, searchStart, searchStart - pos, StringComparison.Ordinal);
               if (idx >= minBreakPos && idx > bestBreak)
               {
                  bestBreak = idx + sep.Length;
                  break;
               }
            }

            if (bestBreak > pos)
               end = bestBreak;
         }

         var chunk = text[pos..end].Trim();
         if (chunk.Length > 0)
            chunks.Add(chunk);

         if (end >= text.Length) break;

         var nextPos = end - chunkOverlap;
         if (nextPos <= pos) nextPos = pos + 1;
         if (nextPos >= text.Length) break;
         pos = nextPos;
      }

      return chunks;
   }
}
