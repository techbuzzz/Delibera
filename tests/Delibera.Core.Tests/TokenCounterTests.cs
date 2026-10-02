using Delibera.Core.Compression;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the token-estimation memo cache (W2-01).
///    <para>
///    The cache used to move a linked-list node on every <em>hit</em> under an exclusive
///    lock, while <see cref="TokenCounter.Default" /> is a process-wide singleton and every
///    debate participant is estimated concurrently. It is now a lock-free CLOCK
///    (second-chance) cache, so these tests pin the properties that change had to keep:
///    the documented capacity bound, the survival of hot keys, and a queue that cannot
///    grow without limit.
///    </para>
/// </summary>
public sealed class TokenCounterTests
{
    private static TokenCounter Counter(int maxEntries = 8) => new()
    {
        MaxMemoizedEntries = maxEntries,
    };

    [Fact]
    public void Estimate_Is_Stable_With_And_Without_Memoization()
    {
        var memoized = Counter();
        var plain = new TokenCounter { MaxMemoizedEntries = 0 };

        for (var i = 0; i < 50; i++)
        {
            var text = $"fragment number {i} with a few words in it";
            memoized.EstimateTokens(text).Should().Be(plain.EstimateTokens(text));
        }
    }

    [Fact]
    public void Estimate_Repeats_Return_The_Memoized_Value()
    {
        var counter = Counter();
        const string text = "the quick brown fox jumps over the lazy dog";

        var first = counter.EstimateTokens(text);
        counter.EstimateTokens(text);
        counter.EstimateTokens(text);

        counter.MemoizedCount.Should().Be(1);
        counter.EstimateTokens(text).Should().Be(first);
    }

    [Fact]
    public void Memoization_Stays_Within_MaxMemoizedEntries()
    {
        var counter = Counter(maxEntries: 8);

        for (var i = 0; i < 500; i++)
            counter.EstimateTokens($"distinct fragment {i}");

        counter.MemoizedCount.Should().BeLessThanOrEqualTo(8,
            "MaxMemoizedEntries is documented as a hard bound, not a hint");
    }

    [Fact]
    public void Eviction_Queue_Does_Not_Grow_Without_Bound()
    {
        // A second-chance re-queue adds an item without removing one, so a workload of
        // uniformly hot keys is the case that would make the queue grow forever.
        var counter = Counter(maxEntries: 8);
        var hot = Enumerable.Range(0, 8).Select(i => $"hot fragment {i}").ToArray();

        for (var round = 0; round < 200; round++)
        {
            foreach (var key in hot)
                counter.EstimateTokens(key); // keep every key "recently used"
            counter.EstimateTokens($"cold fragment {round}");
        }

        counter.EvictionQueueLength.Should().BeLessThanOrEqualTo(4 * 8,
            "the queue is rebuilt once it passes twice the capacity");
        counter.MemoizedCount.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    public void Hot_Keys_Are_Not_Eagerly_Evicted()
    {
        // Second chance is the whole point of the flag: a key that keeps being read should
        // survive a flood of cold ones, unlike strict FIFO.
        var counter = Counter(maxEntries: 4);
        const string hot = "this fragment is read over and over";

        counter.EstimateTokens(hot);
        for (var i = 0; i < 50; i++)
        {
            counter.EstimateTokens(hot);
            counter.EstimateTokens($"cold {i}");
        }

        counter.MemoizedCount.Should().BeLessThanOrEqualTo(4);
        counter.EstimateTokens(hot).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Concurrent_Estimates_Are_Safe_And_Correct()
    {
        // The reason the old implementation was a bottleneck: TokenCounter.Default is
        // shared by every debate in the process and participants are fanned out.
        var counter = Counter(maxEntries: 32);
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < 200; i++)
        {
            var text = $"shared fragment {i % 40} words here";
            expected[text] = counter.EstimateTokens(text);
        }

        var errors = new List<Exception>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 2_000; i++)
                {
                    var text = $"worker {worker} fragment {i % 50} words here";
                    var estimate = counter.EstimateTokens(text);
                    if (estimate <= 0)
                        throw new InvalidOperationException("non-positive estimate");
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        })));

        errors.Should().BeEmpty();
        counter.MemoizedCount.Should().BeLessThanOrEqualTo(32);

        foreach (var (text, value) in expected)
            counter.EstimateTokens(text).Should().Be(value,
                "memoized and freshly computed estimates must not diverge");
    }

    [Fact]
    public void Long_Text_Bypasses_The_Cache()
    {
        var counter = Counter();
        var huge = new string('x', counter.MaxMemoizedLength + 1);

        counter.EstimateTokens(huge).Should().BeGreaterThan(0);
        counter.MemoizedCount.Should().Be(0);
    }

    [Fact]
    public void Null_And_Empty_Input_Returns_Zero()
    {
        var counter = Counter();

        // (string?) cast: an untyped null is ambiguous between the string and
        // IEnumerable<string> overloads, so the compiler forces the intent here.
        counter.EstimateTokens((string?)null).Should().Be(0);
        counter.EstimateTokens(string.Empty).Should().Be(0);
    }
}
