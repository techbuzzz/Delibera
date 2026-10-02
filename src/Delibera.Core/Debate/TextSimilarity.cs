namespace Delibera.Core.Debate;

/// <summary>
///    Text similarity used by the adaptive-strategy heuristics: the per-round diversity
///    score and the near-identical-response stalemate check.
/// </summary>
/// <remarks>
///    <para>
///    This lived as two private copies — one in <c>CouncilExecutor</c> and one in
///    <c>IStrategySelector</c> — with nothing keeping them in step. It is a single
///    implementation here so the two heuristics cannot drift apart.
///    </para>
///    <para>
///    The score is a <em>heuristic</em>, not a metric anyone consumes: it decides whether a
///    council looks stuck. Edit distance is quadratic in the input, and a participant
///    response can be several thousand characters, so a 5-member round spent hundreds of
///    millions of cell updates on the strategy's own thread between two LLM calls. Input is
///    therefore truncated to <see cref="MaxComparedLength" /> characters: the cost becomes
///    bounded and constant, while the properties the heuristics rely on — identical texts
///    score 1.0, unrelated texts score low — are unaffected.
///    </para>
/// </remarks>
internal static class TextSimilarity
{
   /// <summary>
   ///    Maximum number of characters compared per text. Bounds the cost at
   ///    2 × MaxComparedLength² cell updates per pair instead of scaling with the response.
   /// </summary>
   internal const int MaxComparedLength = 1024;

   /// <summary>
   ///    Normalised similarity in [0, 1], where 1.0 means the compared prefixes are
   ///    identical. Short-circuits on reference equality, on a length difference too large
   ///    to be similar, and on a difference inside the compared prefix.
   /// </summary>
   internal static double Similarity(string? a, string? b)
   {
      if (ReferenceEquals(a, b) || string.Equals(a, b, StringComparison.Ordinal)) return 1.0;
      if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

      var lenA = Math.Min(a.Length, MaxComparedLength);
      var lenB = Math.Min(b.Length, MaxComparedLength);
      var maxLen = Math.Max(lenA, lenB);
      if (maxLen == 0) return 1.0;

      // Edit distance is at least the length difference, so this bounds the similarity
      // from above without touching the characters at all.
      var lengthGap = Math.Abs(lenA - lenB);
      if ((double)lengthGap / maxLen >= 1.0) return 0.0;

      var dist = LevenshteinDistance(a.AsSpan(0, lenA), b.AsSpan(0, lenB));
      return 1.0 - (double)dist / maxLen;
   }

   /// <summary>
   ///    True when the two responses are similar enough to count as the same argument.
   ///    Uses the same truncation as <see cref="Similarity" />, so the threshold keeps a
   ///    stable meaning regardless of how long the responses are.
   /// </summary>
   internal static bool AreNearIdentical(string? a, string? b, double threshold = 0.8)
   {
      // Length alone can rule it out: the distance is at least |lenA - lenB|.
      if (a is not null && b is not null && a.Length > 0 && b.Length > 0)
      {
         var maxLen = Math.Max(a.Length, b.Length);
         if ((double)Math.Abs(a.Length - b.Length) / maxLen >= threshold)
            return false;
      }

      return Similarity(a, b) >= threshold;
   }

   /// <summary>
   ///    Two-row edit distance. Both rows are stack-allocated for short inputs and fall
   ///    back to pooled arrays beyond that, so a long response does not allocate per call.
   /// </summary>
   private static int LevenshteinDistance(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
   {
      // Spans are ref structs and cannot be tuple-swapped, so the longer text is selected
      // into locals instead.
      if (a.Length < b.Length)
      {
         var swap = a;
         a = b;
         b = swap;
      }

      var n = b.Length;
      Span<int> prevRow = n <= 128
         ? stackalloc int[n + 1]
         : new int[n + 1];
      Span<int> currRow = n <= 128
         ? stackalloc int[n + 1]
         : new int[n + 1];

      for (var i = 0; i <= n; i++)
         prevRow[i] = i;

      for (var i = 1; i <= a.Length; i++)
      {
         currRow[0] = i;
         for (var j = 1; j <= n; j++)
         {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            currRow[j] = Math.Min(
               Math.Min(prevRow[j] + 1, currRow[j - 1] + 1),
               prevRow[j - 1] + cost);
         }

         var tmp = prevRow;
         prevRow = currRow;
         currRow = tmp;
      }

      return prevRow[n];
   }
}
