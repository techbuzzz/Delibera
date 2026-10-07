namespace Delibera.Core.Debate;

/// <summary>
///    A single member's response changed between two runs of the same debate.
/// </summary>
/// <param name="Member">Display name of the member.</param>
/// <param name="OldText">Response in the baseline debate.</param>
/// <param name="NewText">Response in the comparison debate.</param>
/// <param name="Similarity">
///    Normalised similarity in [0, 1] over the <em>whole</em> text — 1.0 means identical.
///    Deliberately unbounded: the debate diff must not inherit
///    <see cref="TextSimilarity.MaxComparedLength" />, or two long responses differing only
///    at the end would be reported as identical.
/// </param>
/// <param name="InlineDiff">Word-level unified diff, <c>**added**</c> and <c>~~removed~~</c> markup.</param>
public sealed record MemberDiff(
   string Member,
   string OldText,
   string NewText,
   double Similarity,
   string InlineDiff);

/// <summary>
///    One round compared between two runs, plus a flag when the round carried the final synthesis.
/// </summary>
/// <param name="RoundNumber">Round number (1-based), matched across runs rather than by position.</param>
/// <param name="RoundName">Round title from the comparison run.</param>
/// <param name="Members">Per-member diffs for the responses that changed.</param>
/// <param name="MemberSimilarity">
///    Aggregate similarity of the two rounds as a whole, or <c>null</c> when either round
///    carried no responses to compare.
/// </param>
/// <param name="VerdictChanged">Whether this round carried the final synthesis on either side.</param>
public sealed record RoundDiff(
   int RoundNumber,
   string RoundName,
   IReadOnlyList<MemberDiff> Members,
   double? MemberSimilarity,
   bool VerdictChanged);

/// <summary>
///    Side-by-side comparison of two debate runs — the result of
///    <see cref="DebateResultExtensions.Diff(Models.DebateResult, Models.DebateResult)" />.
/// </summary>
/// <remarks>
///    Matching is by <see cref="DebateRound.RoundNumber" /> and by member display name, never
///    by list position. A four-round run compared against a three-round run therefore reports
///    the extra round as missing rather than silently shifting every later comparison onto the
///    wrong response.
/// </remarks>
/// <param name="OldDebateId">Identifier of the baseline debate.</param>
/// <param name="NewDebateId">Identifier of the comparison debate.</param>
/// <param name="VerdictSimilarity">Normalised similarity of the two final verdicts, in [0, 1].</param>
/// <param name="VerdictChanged">Whether the final verdicts differ beyond a trailing-whitespace difference.</param>
/// <param name="Rounds">Per-round comparison, ordered by round number.</param>
/// <param name="MissingRoundNumbers">Round numbers present only in the baseline.</param>
/// <param name="AddedRoundNumbers">Round numbers present only in the comparison debate.</param>
/// <param name="MembersOnlyInOld">Members that appear only in the baseline.</param>
/// <param name="MembersOnlyInNew">Members that appear only in the comparison debate.</param>
public sealed record DebateDiff(
   string OldDebateId,
   string NewDebateId,
   double VerdictSimilarity,
   bool VerdictChanged,
   IReadOnlyList<RoundDiff> Rounds,
   IReadOnlyList<int> MissingRoundNumbers,
   IReadOnlyList<int> AddedRoundNumbers,
   IReadOnlyList<string> MembersOnlyInOld,
   IReadOnlyList<string> MembersOnlyInNew)
{
   /// <summary><c>true</c> when the two runs produced nothing worth reporting.</summary>
   public bool IsEmpty =>
      !VerdictChanged
      && MissingRoundNumbers.Count == 0
      && AddedRoundNumbers.Count == 0
      && MembersOnlyInOld.Count == 0
      && MembersOnlyInNew.Count == 0
      && Rounds.All(r => r.Members.Count == 0 && !r.VerdictChanged);

   /// <summary>
   ///    Renders the comparison as Markdown: a metadata header, then one section per round with
   ///    the changed member responses and their word-level diffs.
   /// </summary>
   public string ToMarkdown()
   {
      var sb = new StringBuilder(4096);

      sb.AppendLine("# Debate Comparison");
      sb.AppendLine();
      sb.AppendLine($"**Baseline:** `{OldDebateId}`");
      sb.AppendLine($"**Comparison:** `{NewDebateId}`");
      sb.AppendLine($"**Verdict similarity:** {VerdictSimilarity:P1}");
      sb.AppendLine();

      if (IsEmpty)
      {
         sb.AppendLine("No differences: identical verdicts and identical member responses.");
         sb.AppendLine();
         return sb.ToString();
      }

      if (MissingRoundNumbers.Count > 0)
         sb.AppendLine($"> ⚠️ Rounds missing from the comparison: {string.Join(", ", MissingRoundNumbers)}");
      if (AddedRoundNumbers.Count > 0)
         sb.AppendLine($"> ⚠️ Rounds only in the comparison: {string.Join(", ", AddedRoundNumbers)}");
      if (MembersOnlyInOld.Count > 0)
         sb.AppendLine($"> ⚠️ Members only in the baseline: {string.Join(", ", MembersOnlyInOld)}");
      if (MembersOnlyInNew.Count > 0)
         sb.AppendLine($"> ⚠️ Members only in the comparison: {string.Join(", ", MembersOnlyInNew)}");
      if (MissingRoundNumbers.Count > 0 || AddedRoundNumbers.Count > 0
          || MembersOnlyInOld.Count > 0 || MembersOnlyInNew.Count > 0)
         sb.AppendLine();

      if (VerdictChanged)
      {
         sb.AppendLine("## Final Verdict — changed");
         sb.AppendLine();
      }

      foreach (var round in Rounds)
      {
         if (round.Members.Count == 0) continue;

         sb.AppendLine($"## Round {round.RoundNumber}: {round.RoundName}");
         sb.AppendLine();

         foreach (var member in round.Members)
         {
            sb.AppendLine($"### {member.Member} — {member.Similarity:P1} similar");
            sb.AppendLine();
            sb.AppendLine(member.InlineDiff);
            sb.AppendLine();
         }
      }

      sb.AppendLine("---");
      sb.AppendLine($"*Generated by Delibera at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}*");

      return sb.ToString();
   }

   /// <summary>
   ///    Renders the comparison as a self-contained HTML document with inline CSS.
   /// </summary>
   public string ToHtml()
   {
      var sb = new StringBuilder(8192);

      sb.AppendLine("<!DOCTYPE html>");
      sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
      sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
      sb.AppendLine("<title>Delibera — Debate Comparison</title>");
      sb.AppendLine("<style>");
      sb.AppendLine("body{font-family:system-ui,-apple-system,'Segoe UI',sans-serif;margin:2rem auto;max-width:60rem;padding:0 1rem;line-height:1.6;color:#1c1c1c}");
      sb.AppendLine("h1{margin-bottom:.25rem}h2{margin-top:2rem;border-bottom:1px solid #e3e3e3;padding-bottom:.25rem}");
      sb.AppendLine("h3{margin-top:1.5rem;font-size:1.05rem}");
      sb.AppendLine("code{background:#f4f4f5;padding:.1rem .3rem;border-radius:3px;font-size:.9em}");
      sb.AppendLine("del{background:#fee2e2;color:#991b1b;text-decoration:line-through;padding:0 .15rem}");
      sb.AppendLine("ins{background:#dcfce7;color:#14532d;text-decoration:none;padding:0 .15rem}");
      sb.AppendLine(".meta{color:#52525b;font-size:.9rem}");
      sb.AppendLine(".warn{background:#fef3c7;border-left:3px solid #f59e0b;padding:.5rem .75rem;margin:.5rem 0}");
      sb.AppendLine(".diff{background:#fafafa;border:1px solid #e3e3e3;border-radius:6px;padding:.75rem;white-space:pre-wrap;word-break:break-word}");
      sb.AppendLine("</style></head><body>");
      sb.AppendLine("<h1>Debate Comparison</h1>");
      sb.AppendLine($"<p class=\"meta\">Baseline <code>{HtmlEncode(OldDebateId)}</code> &rarr; comparison <code>{HtmlEncode(NewDebateId)}</code><br>Verdict similarity: {VerdictSimilarity:P1}</p>");

      if (IsEmpty)
      {
         sb.AppendLine("<p>No differences: identical verdicts and identical member responses.</p>");
      }
      else
      {
         if (MissingRoundNumbers.Count > 0)
            sb.AppendLine($"<div class=\"warn\">Rounds missing from the comparison: {string.Join(", ", MissingRoundNumbers)}</div>");
         if (AddedRoundNumbers.Count > 0)
            sb.AppendLine($"<div class=\"warn\">Rounds only in the comparison: {string.Join(", ", AddedRoundNumbers)}</div>");
         if (MembersOnlyInOld.Count > 0)
            sb.AppendLine($"<div class=\"warn\">Members only in the baseline: {string.Join(", ", MembersOnlyInOld.Select(HtmlEncode))}</div>");
         if (MembersOnlyInNew.Count > 0)
            sb.AppendLine($"<div class=\"warn\">Members only in the comparison: {string.Join(", ", MembersOnlyInNew.Select(HtmlEncode))}</div>");

         foreach (var round in Rounds)
         {
            if (round.Members.Count == 0) continue;

            sb.AppendLine($"<h2>Round {round.RoundNumber}: {HtmlEncode(round.RoundName)}</h2>");
            foreach (var member in round.Members)
            {
               sb.AppendLine($"<h3>{HtmlEncode(member.Member)} — {member.Similarity:P1} similar</h3>");
               sb.AppendLine($"<div class=\"diff\">{InlineDiffToHtml(member.InlineDiff)}</div>");
            }
         }
      }

      sb.AppendLine($"<p class=\"meta\">Generated by Delibera at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}</p>");
      sb.AppendLine("</body></html>");

      return sb.ToString();
   }

   /// <summary>
   ///    Writes <see cref="ToMarkdown" /> to a file.
   /// </summary>
   /// <param name="filePath">Destination path.</param>
   /// <param name="ct">Cancellation token; checked at entry and forwarded to the write.</param>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   public Task SaveToMarkdownAsync(string filePath, CancellationToken ct = default)
   {
      ct.ThrowIfCancellationRequested();
      return File.WriteAllTextAsync(filePath, ToMarkdown(), ct);
   }

   /// <summary>
   ///    Writes <see cref="ToHtml" /> to a file.
   /// </summary>
   /// <param name="filePath">Destination path.</param>
   /// <param name="ct">Cancellation token; checked at entry and forwarded to the write.</param>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   public Task SaveToHtmlAsync(string filePath, CancellationToken ct = default)
   {
      ct.ThrowIfCancellationRequested();
      return File.WriteAllTextAsync(filePath, ToHtml(), ct);
   }

   private static string HtmlEncode(string value) => System.Net.WebUtility.HtmlEncode(value);

   /// <summary>
   ///    Converts the Markdown inline diff produced by <see cref="DebateResultExtensions" />
   ///    into HTML, mapping the agreed markers onto <c>&lt;del&gt;</c> / <c>&lt;ins&gt;</c>.
   ///    Everything else is HTML-encoded, so a member response containing angle brackets or
   ///    an ampersand cannot inject markup into the exported document.
   /// </summary>
   private static string InlineDiffToHtml(string markdownDiff)
   {
      var sb = new StringBuilder(markdownDiff.Length + 64);

      // Split on the markers, keeping them, so each span is encoded independently.
      var index = 0;
      while (index < markdownDiff.Length)
      {
         var next = markdownDiff.IndexOfAny(MarkerChars, index);
         if (next < 0)
         {
            sb.Append(HtmlEncode(markdownDiff[index..]));
            break;
         }

         if (next > index)
            sb.Append(HtmlEncode(markdownDiff[index..next]));

         if (markdownDiff.AsSpan(next).StartsWith("**", StringComparison.Ordinal))
         {
            var close = markdownDiff.IndexOf("**", next + 2, StringComparison.Ordinal);
            if (close < 0)
            {
               sb.Append(HtmlEncode(markdownDiff[next..]));
               break;
            }

            sb.Append("<ins>").Append(HtmlEncode(markdownDiff[(next + 2)..close])).Append("</ins>");
            index = close + 2;
         }
         else
         {
            var close = markdownDiff.IndexOf("~~", next + 2, StringComparison.Ordinal);
            if (close < 0)
            {
               sb.Append(HtmlEncode(markdownDiff[next..]));
               break;
            }

            sb.Append("<del>").Append(HtmlEncode(markdownDiff[(next + 2)..close])).Append("</del>");
            index = close + 2;
         }
      }

      return sb.ToString();
   }

   private static readonly char[] MarkerChars = ['*', '~'];
}