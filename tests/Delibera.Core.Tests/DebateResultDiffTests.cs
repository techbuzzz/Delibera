using Delibera.Core.Debate;
using Delibera.Core.Models;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    I-02 — <c>DebateResult.Diff</c> compares two runs of the same question.
///
///    Two properties matter more than the rendering. First, matching is by round number and
///    member name, never by list position: comparing a four-round run against a three-round run
///    must report the missing round, not shift every later comparison onto the wrong response.
///    Second, the similarity score covers the whole text. Reusing the adaptive-strategy
///    heuristic as-is would cap the comparison at 1024 characters and report two long verdicts
///    that differ only at the end as identical.
/// </summary>
public sealed class DebateResultDiffTests
{
   [Fact]
   public void Diff_On_identical_results_Is_empty_and_fully_similar()
   {
      var left = Result("a", [Round(1, "Opening", ("alice", "Ship it"))], "Ship it");
      var right = Result("b", [Round(1, "Opening", ("alice", "Ship it"))], "Ship it");

      var diff = left.Diff(right);

      diff.IsEmpty.Should().BeTrue();
      diff.VerdictChanged.Should().BeFalse();
      diff.VerdictSimilarity.Should().Be(1.0);
      diff.Rounds.Should().ContainSingle().Which.Members.Should().BeEmpty();
   }

   [Fact]
   public void Diff_Reports_A_Missing_Round_Instead_Of_Shifting_Comparisons()
   {
      var left = Result("a",
      [
         Round(1, "Opening", ("alice", "Round one text")),
         Round(2, "Critique", ("alice", "Round two text")),
         Round(3, "Verdict", ("alice", "Round three text"))
      ], "done");

      var right = Result("b",
      [
         Round(1, "Opening", ("alice", "Round one text")),
         Round(2, "Critique", ("alice", "Round two text"))
      ], "done");

      var diff = left.Diff(right);

      diff.MissingRoundNumbers.Should().ContainSingle().Which.Should().Be(3);
      diff.AddedRoundNumbers.Should().BeEmpty();
      // Rounds 1 and 2 matched by number, so their unchanged responses produce no member diffs.
      diff.Rounds.Should().HaveCount(2);
      diff.Rounds.Select(r => r.RoundNumber).Should().ContainInOrder(1, 2);
      diff.Rounds.Should().OnlyContain(r => r.Members.Count == 0);
   }

   [Fact]
   public void Diff_Reports_An_Added_Round()
   {
      var left = Result("a", [Round(1, "Opening", ("alice", "one"))], "v");
      var right = Result("b",
      [
         Round(1, "Opening", ("alice", "one")),
         Round(2, "Extra", ("alice", "two"))
      ], "v");

      right.Diff(left).MissingRoundNumbers.Should().ContainSingle().Which.Should().Be(2);
   }

   [Fact]
   public void Diff_Reports_Members_That_Exist_On_Only_One_Side()
   {
      var left = Result("a", [Round(1, "Opening", ("alice", "same"), ("bob", "left only"))], "v");
      var right = Result("b", [Round(1, "Opening", ("alice", "same"), ("carol", "right only"))], "v");

      var diff = left.Diff(right);

      diff.MembersOnlyInOld.Should().ContainSingle().Which.Should().Be("bob");
      diff.MembersOnlyInNew.Should().ContainSingle().Which.Should().Be("carol");
   }

   [Fact]
   public void Diff_Produces_a_Word_Level_Markdown_Inline_Diff()
   {
      var left = Result("a", [Round(1, "Opening", ("alice", "we should ship on friday"))], "v");
      var right = Result("b", [Round(1, "Opening", ("alice", "we should ship on monday"))], "v");

      var diff = left.Diff(right);
      var member = diff.Rounds.Should().ContainSingle().Subject.Members.Should().ContainSingle().Subject;

      member.InlineDiff.Should().Contain("friday");
      member.InlineDiff.Should().Contain("monday");
      member.InlineDiff.Should().Contain("~~friday~~").And.Contain("**monday**");
   }

   [Fact]
   public void Diff_Similarity_Is_Not_Capped_At_The_Heuristic_Length()
   {
      // Two verdicts longer than TextSimilarity.MaxComparedLength (1024) that differ only in the
      // final word. A capped comparison would ignore the tail and report 1.0 — a false "identical".
      var prefix = string.Join(' ', Enumerable.Repeat("the council weighed the options carefully", 40));
      prefix.Length.Should().BeGreaterThan(TextSimilarity.MaxComparedLength);

      var diff = Result("a", [], $"{prefix} therefore approve")
         .Diff(Result("b", [], $"{prefix} therefore reject"));

      diff.VerdictChanged.Should().BeTrue();
      diff.VerdictSimilarity.Should().BeLessThan(1.0,
         "a difference beyond the 1024-character heuristic cap must still register");
   }

   [Fact]
   public void Diff_ToMarkdown_Renders_A_Missing_Round_Warning()
   {
      var left = Result("a",
      [
         Round(1, "Opening", ("alice", "one")),
         Round(2, "Verdict", ("alice", "two"))
      ], "done");
      var right = Result("b", [Round(1, "Opening", ("alice", "one"))], "done");

      var markdown = left.Diff(right).ToMarkdown();

      markdown.Should().Contain("# Debate Comparison");
      markdown.Should().Contain("Rounds missing from the comparison: 2");
   }

   [Fact]
   public void Diff_ToHtml_Escapes_Response_Content()
   {
      var left = Result("a", [Round(1, "Opening", ("alice", "plain text"))], "v");
      var right = Result("b", [Round(1, "Opening", ("alice", "<script>alert(1)</script>"))], "v");

      var html = left.Diff(right).ToHtml();

      html.Should().NotContain("<script>alert(1)</script>",
         "a member response must not be able to inject markup into the exported document");
      html.Should().Contain("&lt;script&gt;");
   }

   [Fact]
   public void Diff_Treats_A_Verdict_That_Only_Differs_In_Trailing_Whitespace_As_Unchanged()
   {
      var left = Result("a", [], "ship it");
      var right = Result("b", [], "ship it\n");

      left.Diff(right).VerdictChanged.Should().BeFalse();
   }

   private static DebateResult Result(string id, IReadOnlyList<DebateRound> rounds, string verdict) => new()
   {
      DebateId = id,
      StrategyName = "Standard",
      Context = new PromptContext(SystemPrompt: "sys", UserPrompt: "user"),
      Participants = ["alice"],
      Rounds = rounds,
      FinalVerdict = verdict,
      StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
      CompletedAt = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc)
   };

   private static DebateRound Round(int number, string name, params (string Member, string Text)[] responses) => new()
      {
         RoundNumber = number,
         Total = 2,
         RoundName = name,
         Responses = responses.ToDictionary(r => r.Member, r => r.Text, StringComparer.Ordinal)
      };
}