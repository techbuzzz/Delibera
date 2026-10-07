using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    W2-09 — participant fan-out must respect the configured parallelism cap.
///    <para>
///    The operator and knowledge-keeper paths already honoured
///    <c>DebateExecutionOptions.MaxDegreeOfParallelism</c>; the participant fan-out used a
///    bare <c>Task.WhenAll</c>, so a large council could open as many provider calls at once
///    as it had members — exhausting the HttpClient socket pool and multiplying rate-limit
///    errors.
///    </para>
/// </summary>
public sealed class DebateParallelismTests
{
    /// <summary>Provider that records the peak number of concurrent calls.</summary>
    private sealed class ConcurrencyProbeProvider(int delayMs = 40) : ILLMProvider
    {
        private int _current;
        private int _peak;

        public int ChatCallCount;

        public int PeakConcurrency => _peak;

        public string ProviderName => "Probe";

        public async Task<string> ChatAsync(
            string model, string systemPrompt, string userPrompt,
            float temperature = 0.7f, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ChatCallCount);
            var now = Interlocked.Increment(ref _current);
            InterlockedMax(ref _peak, now);

            try
            {
               await Task.Delay(delayMs, ct);
               return "reply";
            }
            finally
            {
               Interlocked.Decrement(ref _current);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value)
               if (Interlocked.CompareExchange(ref target, value, current) == current)
                  return;
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["probe-model"]);

        public Task<ModelCapabilities> GetModelCapabilitiesAsync(
            string model, CancellationToken ct = default)
            => Task.FromResult(ModelCapabilities.Unknown(model));

        public void Dispose() { }
    }

    private static ICouncilBuilder Builder(ILLMProvider provider, int maxDegreeOfParallelism)
    {
        return new CouncilBuilder()
            .AddMember("probe-model", provider, "A")
            .AddMember("probe-model", provider, "B")
            .AddMember("probe-model", provider, "C")
            .AddMember("probe-model", provider, "D")
            .AddMember("probe-model", provider, "E")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithMaxDegreeOfParallelism(maxDegreeOfParallelism);
    }

    [Fact]
    public async Task Unbounded_By_Default_Still_Lets_Members_Run_Concurrently()
    {
        var provider = new ConcurrencyProbeProvider();
        var executor = Builder(provider, maxDegreeOfParallelism: 0).Build();

        await executor.ExecuteAsync();

        // The default is unbounded; the guard is that the cap is *opt-in*, not that it
        // accidentally serialises every debate.
        provider.PeakConcurrency.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task MaxDegreeOfParallelism_Bounds_The_Participant_Fan_Out()
    {
        var provider = new ConcurrencyProbeProvider();
        var executor = Builder(provider, maxDegreeOfParallelism: 2).Build();

        await executor.ExecuteAsync();

        provider.ChatCallCount.Should().BeGreaterThan(0);
        provider.PeakConcurrency.Should().BeLessThanOrEqualTo(2,
            "five members with a cap of two must never have three calls in flight");
    }

    [Fact]
    public async Task Cap_Of_One_Serialises_The_Fan_Out()
    {
        var provider = new ConcurrencyProbeProvider(delayMs: 10);
        var executor = Builder(provider, maxDegreeOfParallelism: 1).Build();

        await executor.ExecuteAsync();

        provider.PeakConcurrency.Should().Be(1);
    }

    [Fact]
    public async Task Every_Member_Still_Produces_A_Response_Under_The_Cap()
    {
        // The cap must bound concurrency, not drop members.
        var provider = new ConcurrencyProbeProvider(delayMs: 5);
        var executor = Builder(provider, maxDegreeOfParallelism: 2).Build();

        var result = await executor.ExecuteAsync();

        result.Rounds.Should().NotBeEmpty();
        result.Rounds[0].Responses.Should().HaveCount(5,
            "all five members answered, despite the cap of two");
    }

    [Fact]
    public async Task Order_Of_Responses_Follows_Member_Order_Not_Completion_Order()
    {
        // The cap is implemented with a semaphore precisely so Task.WhenAll keeps its
        // ordering guarantee — the response keys get "#2" suffixes in member order, and
        // those suffixes must not shuffle between runs.
        var provider = new ConcurrencyProbeProvider(delayMs: 5);
        var executor = Builder(provider, maxDegreeOfParallelism: 2).Build();

        var result = await executor.ExecuteAsync();

        var keys = result.Rounds[0].Responses.Keys.ToArray();
        keys.Should().HaveCount(5);
        keys.Should().BeInAscendingOrder(StringComparer.Ordinal,
            "keys are suffixed in member order, so they stay ordinal-sorted");
    }
}
