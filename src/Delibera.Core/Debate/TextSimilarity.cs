using System.Buffers;

namespace Delibera.Core.Debate;

/// <summary>
///    Text similarity used by the adaptive-strategy heuristics: the per-round diversity
///    score and the near-identical-response stalemate check. Also used by
///    <c>DebateResult.Diff</c> to score how far two debates drifted apart.
/// </summary>
/// <remarks>
///    <para>
///    This lived as two private copies — one in <c>CouncilExecutor</c> and one in
///    <c>IStrategySelector</c> — with nothing keeping them in step. It is a single
///    implementation here so the two heuristics cannot drift apart.
///    </para>
///    <para>
///    The heuristics score is a <em>heuristic</em>, not a metric anyone consumes: it decides
///    whether a council looks stuck. Edit distance is quadratic in the input, and a participant
///    response can be several thousand characters, so a 5-member round spent hundreds of
///    millions of cell updates on the strategy's own thread between two LLM calls. Those
///    callers therefore keep the default <see cref="MaxComparedLength" /> cap: the cost becomes
///    bounded and constant, while the properties the heuristics rely on — identical texts score
///    1.0, unrelated texts score low — are unaffected.
///    </para>
///    <para>
///    A caller that needs the score to mean something over the <em>whole</em> text passes an
///    explicit maximum length (the debate diff passes <see cref="int.MaxValue" />).
///    Quietly inheriting the heuristic cap there would report two long verdicts that differ at
///    the end as "similar" purely because the comparison stopped early.
///    </para>
/// </remarks>
public static class TextSimilarity
{
   /// <summary>
   ///    Default maximum number of characters compared per text. Bounds the cost at
   ///    2 × MaxComparedLength² cell updates per pair instead of scaling with the response.
   /// </summary>
   public const int MaxComparedLength = 1024;

   /// <summary>
   ///    Normalised similarity in [0, 1], where 1.0 means the compared prefixes are
   ///    identical. Short-circuits on reference equality, on a length difference too large
   ///    to be similar, and on a difference inside the compared prefix.
   /// </summary>
   /// <param name="a">First text.</param>
   /// <param name="b">Second text.</param>
   /// <param name="maxLength">
   ///    Maximum characters taken from each side. Pass <see cref="int.MaxValue" /> to compare
   ///    the full text; the default keeps the cost bounded for the adaptive-strategy heuristics.
   /// </param>
   public static double Similarity(string? a, string? b, int maxLength = MaxComparedLength)
   {
      if (ReferenceEquals(a, b) || string.Equals(a, b, StringComparison.Ordinal)) return 1.0;
      if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

      var lenA = Math.Min(a.Length, maxLength);
      var lenB = Math.Min(b.Length, maxLength);
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
   ///    Uses the same truncation as <see cref="Similarity(string, string, int)" /> with the
   ///    default cap, so the threshold keeps a stable meaning regardless of how long the
   ///    responses are.
   /// </summary>
   public static bool AreNearIdentical(string? a, string? b, double threshold = 0.8)
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
   ///    Two-row edit distance. Rows are stack-allocated for short inputs and
   ///    <see cref="ArrayPool{T}" />-rented beyond that, so a long response does not allocate
   ///    two fresh <c>int[n + 1]</c> buffers per call. With <see cref="MaxComparedLength" />
   ///    capping n at 1024, the pooled path is the common one for real participant responses —
   ///    at ~4 KiB per row it would otherwise be ~8 KiB of Gen0 garbage per comparison, on a
   ///    per-round O(n²) loop.
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

      if (n <= StackallocRowThreshold)
         return LevenshteinOnStack(a, b, n);

      // Rented rather than allocated: this runs inside the per-round response-similarity loop.
      var prev = ArrayPool<int>.Shared.Rent(n + 1);
      var curr = ArrayPool<int>.Shared.Rent(n + 1);
      try
      {
         return LevenshteinRows(a, b, n, prev.AsSpan(0, n + 1), curr.AsSpan(0, n + 1));
      }
      finally
      {
         // int holds no references, so the buffers are returned without clearing.
         ArrayPool<int>.Shared.Return(prev);
         ArrayPool<int>.Shared.Return(curr);
      }
   }

   /// <summary>
   ///    Row length at or below which both rows are stack-allocated instead of rented.
   /// </summary>
   private const int StackallocRowThreshold = 128;

   private static int LevenshteinOnStack(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int n)
   {
      // The stackalloc stays inside this method on purpose: a Span created in a block that
      // outlives it cannot be assigned to a variable in the caller (CS8353).
      Span<int> prevRow = stackalloc int[n + 1];
      Span<int> currRow = stackalloc int[n + 1];
      return LevenshteinRows(a, b, n, prevRow, currRow);
   }

   private static int LevenshteinRows(
      ReadOnlySpan<char> a, ReadOnlySpan<char> b, int n, Span<int> prevRow, Span<int> currRow)
   {
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
