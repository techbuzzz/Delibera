using Delibera.Core.Debate;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    W2-04 — the adaptive-strategy similarity heuristics.
///    <para>
///    Edit distance is quadratic. These tests pin the two properties the heuristics
///    actually depend on, and the bound that makes the cost constant regardless of how
///    long a participant's response is.
///    </para>
/// </summary>
public sealed class TextSimilarityTests
{
    [Theory]
    [InlineData("identical text", "identical text")]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData(null, null)]
    public void Identical_Inputs_Score_One(string? a, string? b)
    {
        // Two nulls are identical, not unrelated — this matches the original
        // `if (a == b) return 1.0;` behaviour the heuristics were written against.
        TextSimilarity.Similarity(a, b).Should().Be(1.0);
    }

    [Theory]
    [InlineData("", "non-empty")]
    [InlineData("non-empty", "")]
    [InlineData(null, "text")]
    public void Empty_Or_Null_Inputs_Score_Zero(string? a, string? b)
    {
        TextSimilarity.Similarity(a, b).Should().Be(0.0);
    }

    [Fact]
    public void Unrelated_Texts_Score_Low()
    {
        var score = TextSimilarity.Similarity(
            "The database migration should be rolled out gradually",
            "Quantum entanglement requires careful experimental isolation");

        score.Should().BeLessThan(0.4);
    }

    [Fact]
    public void Small_Edit_Scores_High()
    {
        var score = TextSimilarity.Similarity(
            "The migration should be rolled out gradually",
            "The migration should be rolled out gradually!");

        score.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Similarity_Is_Symmetric()
    {
        const string a = "one two three four five";
        const string b = "one two three four six";

        TextSimilarity.Similarity(a, b).Should().BeApproximately(
            TextSimilarity.Similarity(b, a), 1e-9);
    }

    [Fact]
    public void Very_Lengthy_Inputs_Return_Quickly_And_Stay_Bounded()
    {
        // The cost this guards: before the fix, a pair of 20 000-character responses cost
        // 400 million cell updates, per pair, on the strategy's own thread.
        var a = new string('a', 20_000);
        var b = new string('a', 19_999) + "b";

        var started = System.Diagnostics.Stopwatch.StartNew();
        var score = TextSimilarity.Similarity(a, b);
        started.Stop();

        score.Should().BeInRange(0.0, 1.0);
        // Comparing only MaxComparedLength characters makes this independent of the
        // remaining 19 000 characters. A generous ceiling still catches a regression to
        // the unbounded O(len²) version.
        started.ElapsedMilliseconds.Should().BeLessThan(250,
            "similarity must not scale with the full response length");
    }

    [Fact]
    public void Truncation_Does_Not_Hide_A_Difference_Inside_The_Compared_Window()
    {
        var a = new string('x', 5_000) + "unrelated tail";
        var b = new string('x', 5_000) + "different tail";

        // The first MaxComparedLength characters are identical, so the truncated score is
        // high — which is exactly the documented trade: the heuristic looks at the head of
        // the response, not its whole length.
        TextSimilarity.Similarity(a, b).Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void AreNearIdentical_Agrees_With_The_Similarity_Threshold()
    {
        const string a = "We should adopt the strangler fig pattern for the migration";

        TextSimilarity.AreNearIdentical(a, a).Should().BeTrue();
        TextSimilarity.AreNearIdentical(a, a + " ").Should().BeTrue();
        TextSimilarity.AreNearIdentical(
            a, "The billing ledger disagrees with the reconciliation job").Should().BeFalse();
    }

    [Fact]
    public void AreNearIdentical_Rules_Out_Pairs_By_Length_Alone()
    {
        // A cheap bound, but a correct one: edit distance is at least |lenA - lenB|, so a
        // large length gap cannot reach the threshold and never needs the character loop.
        var brief = "short";
        var verbose = new string('y', 10_000);

        TextSimilarity.AreNearIdentical(brief, verbose).Should().BeFalse();
    }

    [Fact]
    public void AreNearIdentical_Handles_Empty_Inputs()
    {
        TextSimilarity.AreNearIdentical("", "").Should().BeTrue();
        TextSimilarity.AreNearIdentical("", "text").Should().BeFalse();
    }
}
