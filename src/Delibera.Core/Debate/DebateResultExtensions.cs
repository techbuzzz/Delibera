using Delibera.Core.Models;

namespace Delibera.Core.Debate;

/// <summary>
///    Side-by-side comparison of two debate runs of the same question.
/// </summary>
public static class DebateResultExtensions
{
   /// <summary>
   ///    Compares two debate results and reports how far apart they are: a word-level diff of
   ///    every member response that changed, a similarity score for the final verdicts, and
   ///    explicit lists of rounds or members that exist on only one side.
   /// </summary>
   /// <param name="left">The baseline result.</param>
   /// <param name="right">The result to compare against it.</param>
   /// <returns>A <see cref="DebateDiff" /> describing the differences.</returns>
   /// <remarks>
   ///    Rounds are matched on <see cref="DebateRound.RoundNumber" /> and members on display
   ///    name, never on list position. Comparing a four-round run against a three-round run
   ///    therefore reports the extra round instead of shifting every later response onto the
   ///    wrong member. A round present on one side only is reported in
   ///    <see cref="DebateDiff.MissingRoundNumbers" /> / <see cref="DebateDiff.AddedRoundNumbers" />
   ///    rather than being silently skipped.
   /// </remarks>
   public static DebateDiff Diff(this DebateResult left, DebateResult right)
   {
      ArgumentNullException.ThrowIfNull(left);
      ArgumentNullException.ThrowIfNull(right);

      var leftRounds = left.Rounds.ToDictionary(r => r.RoundNumber);
      var rightRounds = right.Rounds.ToDictionary(r => r.RoundNumber);

      var missing = leftRounds.Keys.Except(rightRounds.Keys).OrderBy(n => n).ToList();
      var added = rightRounds.Keys.Except(leftRounds.Keys).OrderBy(n => n).ToList();
      var shared = leftRounds.Keys.Intersect(rightRounds.Keys).OrderBy(n => n).ToList();

      var roundDiffs = new List<RoundDiff>(shared.Count);
      foreach (var roundNumber in shared)
      {
         var leftRound = leftRounds[roundNumber];
         var rightRound = rightRounds[roundNumber];

         var leftMembers = leftRound.Responses.Keys.ToHashSet(StringComparer.Ordinal);
         var rightMembers = rightRound.Responses.Keys.ToHashSet(StringComparer.Ordinal);

         var memberDiffs = new List<MemberDiff>();
         foreach (var member in leftMembers.Intersect(rightMembers, StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal))
         {
            var oldText = leftRound.Responses[member];
            var newText = rightRound.Responses[member];

            if (string.Equals(oldText, newText, StringComparison.Ordinal)) continue;

            memberDiffs.Add(new MemberDiff(
               member,
               oldText,
               newText,
               TextSimilarity.Similarity(oldText, newText, int.MaxValue),
               BuildInlineDiff(oldText, newText)));
         }

         // Per-round aggregate: how similar the rounds are as a whole, so a round can be ranked
         // without re-reading every member diff.
         double? roundSimilarity = null;
         if (leftRound.Responses.Count > 0 && rightRound.Responses.Count > 0)
         {
            roundSimilarity = TextSimilarity.Similarity(
               JoinResponses(leftRound.Responses),
               JoinResponses(rightRound.Responses),
               int.MaxValue);
         }

         var roundName = string.IsNullOrWhiteSpace(rightRound.RoundName)
            ? leftRound.RoundName
            : rightRound.RoundName;

         roundDiffs.Add(new RoundDiff(
            roundNumber,
            roundName,
            memberDiffs,
            roundSimilarity,
            leftRound.IsFinal || rightRound.IsFinal));
      }

      var oldVerdict = left.FinalVerdict;
      var newVerdict = right.FinalVerdict;
      var verdictSimilarity = TextSimilarity.Similarity(oldVerdict, newVerdict, int.MaxValue);
      var verdictChanged = !string.Equals(
         oldVerdict?.Trim(),
         newVerdict?.Trim(),
         StringComparison.Ordinal);

      var allOldMembers = leftRounds.Values
         .SelectMany(r => r.Responses.Keys)
         .ToHashSet(StringComparer.Ordinal);
      var allNewMembers = rightRounds.Values
         .SelectMany(r => r.Responses.Keys)
         .ToHashSet(StringComparer.Ordinal);

      return new DebateDiff(
         left.DebateId,
         right.DebateId,
         verdictSimilarity,
         verdictChanged,
         roundDiffs,
         missing,
         added,
         allOldMembers.Except(allNewMembers, StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList(),
         allNewMembers.Except(allOldMembers, StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList());
   }

   private static string JoinResponses(IReadOnlyDictionary<string, string> responses)
   {
      var sb = new StringBuilder();
      foreach (var (member, response) in responses.OrderBy(kv => kv.Key, StringComparer.Ordinal))
      {
         sb.Append(member).Append('\n').Append(response).Append('\n');
      }

      return sb.ToString();
   }

   /// <summary>
   ///    Word-level unified diff, rendered as <c>**added**</c> and <c>~~removed~~</c> so it can
   ///    be read as Markdown and mapped onto <c>&lt;ins&gt;</c> / <c>&lt;del&gt;</c> in the HTML
   ///    export.
   /// </summary>
   /// <remarks>
   ///    Word-level rather than character-level on purpose: a chairman verdict is prose, and a
   ///    character diff of prose produces noise that hides the one sentence that actually
   ///    changed. A longest-common-subsequence backtrack over tokens keeps the removed/added
   ///    spans minimal.
   /// </remarks>
   private static string BuildInlineDiff(string oldText, string newText)
   {
      var oldWords = Tokenize(oldText);
      var newWords = Tokenize(newText);

      // Drop the common prefix/suffix first: two verdicts usually differ in the middle, and the
      // LCS table is quadratic in what is left.
      var prefix = 0;
      while (prefix < oldWords.Count && prefix < newWords.Count
             && string.Equals(oldWords[prefix], newWords[prefix], StringComparison.Ordinal))
      {
         prefix++;
      }

      var suffix = 0;
      while (suffix < oldWords.Count - prefix
             && suffix < newWords.Count - prefix
             && string.Equals(
                oldWords[^(suffix + 1)],
                newWords[^(suffix + 1)],
                StringComparison.Ordinal))
      {
         suffix++;
      }

      var oldMiddle = oldWords.GetRange(prefix, oldWords.Count - prefix - suffix);
      var newMiddle = newWords.GetRange(prefix, newWords.Count - prefix - suffix);

      var sb = new StringBuilder();
      AppendRange(sb, oldWords, 0, prefix);

      if (oldMiddle.Count == 0 && newMiddle.Count == 0)
      {
         AppendRange(sb, oldWords, prefix, oldWords.Count);
      }
      else
      {
         var script = DiffScript(oldMiddle, newMiddle);
         foreach (var (kind, word) in script)
         {
            switch (kind)
            {
               case DiffKind.Unchanged:
                  sb.Append(word).Append(' ');
                  break;
               case DiffKind.Removed:
                  sb.Append("~~").Append(word).Append("~~ ");
                  break;
               default:
                  sb.Append("**").Append(word).Append("** ");
                  break;
            }
         }
      }

      // Re-attach the common suffix without diff markers.
      var tailStart = oldWords.Count - suffix;
      for (var i = tailStart; i < oldWords.Count; i++)
      {
         if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
         sb.Append(oldWords[i]);
      }

      return sb.ToString().TrimEnd();
   }

   private enum DiffKind
   {
      Unchanged,
      Removed,
      Added
   }

   /// <summary>
   ///    Longest-common-subsequence diff over token lists. Returns the edit script that turns
   ///    <paramref name="oldWords" /> into <paramref name="newWords" />.
   /// </summary>
   private static List<(DiffKind Kind, string Word)> DiffScript(List<string> oldWords, List<string> newWords)
   {
      var rows = oldWords.Count + 1;
      var cols = newWords.Count + 1;
      var table = new int[rows, cols];

      for (var i = oldWords.Count - 1; i >= 0; i--)
      {
         for (var j = newWords.Count - 1; j >= 0; j--)
         {
            table[i, j] = string.Equals(oldWords[i], newWords[j], StringComparison.Ordinal)
               ? table[i + 1, j + 1] + 1
               : Math.Max(table[i + 1, j], table[i, j + 1]);
         }
      }

      var script = new List<(DiffKind, string)>((oldWords.Count + newWords.Count) / 2 + 4);
      var x = 0;
      var y = 0;
      while (x < oldWords.Count && y < newWords.Count)
      {
         if (string.Equals(oldWords[x], newWords[y], StringComparison.Ordinal))
         {
            script.Add((DiffKind.Unchanged, oldWords[x]));
            x++;
            y++;
         }
         else if (table[x + 1, y] >= table[x, y + 1])
         {
            script.Add((DiffKind.Removed, oldWords[x]));
            x++;
         }
         else
         {
            script.Add((DiffKind.Added, newWords[y]));
            y++;
         }
      }

      while (x < oldWords.Count) script.Add((DiffKind.Removed, oldWords[x++]));
      while (y < newWords.Count) script.Add((DiffKind.Added, newWords[y++]));

      return script;
   }

   private static void AppendRange(StringBuilder sb, List<string> words, int start, int end)
   {
      for (var i = start; i < end; i++)
         sb.Append(words[i]).Append(' ');
   }

   /// <summary>
   ///    Splits on whitespace, keeping each word with its trailing whitespace so the rebuilt
   ///    diff preserves the original line breaks instead of collapsing the text into one block.
   /// </summary>
   private static List<string> Tokenize(string text)
   {
      var tokens = new List<string>();
      var start = 0;

      for (var i = 0; i < text.Length; i++)
      {
         if (!char.IsWhiteSpace(text[i])) continue;

         if (i > start) tokens.Add(text[start..i]);
         while (i + 1 < text.Length && char.IsWhiteSpace(text[i + 1])) i++;
         start = i + 1;
      }

      if (start < text.Length) tokens.Add(text[start..]);

      return tokens;
   }
}