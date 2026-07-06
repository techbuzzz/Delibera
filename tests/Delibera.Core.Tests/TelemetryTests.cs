using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Delibera.Core.Council;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Telemetry;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Delibera.Core.Tests;

/// <summary>
///    Serializes tests touching the static <see cref="DeliberaActivitySource"/> and
///    <see cref="DeliberaMeter"/> singletons so parallel xUnit execution does not
///    cause cross-test interference (one test's <c>Shutdown()</c> would null another's
///    cached <c>ActivitySource</c>).
/// </summary>
[Collection("Telemetry")]
public class TelemetryTests
{
    [Fact]
    public void TelemetryOptions_Defaults_Are_Disabled_With_Canonical_Names()
    {
        var opts = new TelemetryOptions();

        opts.Enabled.Should().BeFalse();
        opts.ActivitySourceName.Should().Be(DeliberaActivitySource.DefaultName);
        opts.MeterName.Should().Be(DeliberaMeter.DefaultName);
        opts.ServiceVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void StartActivity_Without_Listener_Returns_Null()
    {
        // No ActivityListener installed — the canonical zero-overhead path.
        var activity = DeliberaActivitySource.StartActivity("test.no-listener");
        activity.Should().BeNull();
    }

    [Fact]
    public void StartActivity_With_Listener_Returns_Activity_With_Tags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == DeliberaActivitySource.DefaultName,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = DeliberaActivitySource.StartActivity(
            "test.with-listener",
            [new("tag.key", "tag.value")]);

        activity.Should().NotBeNull();
        activity!.DisplayName.Should().Be("test.with-listener");
        activity.GetTagItem("tag.key").Should().Be("tag.value");
    }

    [Fact]
    public void StartActivity_With_Single_Tag_Overload_Attaches_Tag()
    {
        using var listener = CreateEnabledListener();
        ActivitySource.AddActivityListener(listener);

        using var activity = DeliberaActivitySource.StartActivity(
            "test.single-tag", "k", "v");

        activity.Should().NotBeNull();
        activity!.GetTagItem("k").Should().Be("v");
    }

    [Fact]
    public void Configure_Overrides_Activity_Source_Name()
    {
        try
        {
            DeliberaActivitySource.Configure("Test.Custom.Source", "9.9.9");

            using var listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == "Test.Custom.Source",
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };
            ActivitySource.AddActivityListener(listener);

            using var activity = DeliberaActivitySource.StartActivity("custom.op");
            activity.Should().NotBeNull();
            activity!.Source.Name.Should().Be("Test.Custom.Source");
            activity.Source.Version.Should().Be("9.9.9");
        }
        finally
        {
            // Always reset to defaults so subsequent tests see the canonical source name.
            DeliberaActivitySource.Configure(DeliberaActivitySource.DefaultName, "1.0.0");
        }
    }

    [Fact]
    public void DeliberaTelemetry_StartDebate_Attaches_Canonical_Tags()
    {
        using var listener = CreateEnabledListener();
        ActivitySource.AddActivityListener(listener);

        using var activity = DeliberaTelemetry.StartDebate(
            debateId: "deb-123",
            strategyName: "Standard",
            memberCount: 3,
            maxRounds: 4);

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be(DeliberaActivityNames.CouncilExecute);
        activity.GetTagItem(DeliberaTelemetryTags.DebateId).Should().Be("deb-123");
        activity.GetTagItem(DeliberaTelemetryTags.StrategyName).Should().Be("Standard");
        activity.GetTagItem("debate.member_count").Should().Be(3);
        activity.GetTagItem("debate.max_rounds").Should().Be(4);
    }

    [Fact]
    public void DeliberaTelemetry_StartRound_Attaches_Round_Tags()
    {
        using var listener = CreateEnabledListener();
        ActivitySource.AddActivityListener(listener);

        using var activity = DeliberaTelemetry.StartRound(2, "Critique");

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be(DeliberaActivityNames.CouncilRound);
        activity.GetTagItem(DeliberaTelemetryTags.RoundNumber).Should().Be(2);
        activity.GetTagItem(DeliberaTelemetryTags.RoundName).Should().Be("Critique");
    }

    [Fact]
    public void DeliberaTelemetry_MarkFailed_Sets_Error_Status_And_Tags()
    {
        using var listener = CreateEnabledListener();
        ActivitySource.AddActivityListener(listener);

        using var activity = DeliberaTelemetry.StartDebate("d", "s", 1, 1);
        DeliberaTelemetry.MarkFailed(activity, "boom");

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("boom");
        activity.GetTagItem(DeliberaTelemetryTags.Success).Should().Be("false");
        activity.GetTagItem(DeliberaTelemetryTags.ErrorMessage).Should().Be("boom");
    }

    [Fact]
    public void DeliberaTelemetry_MarkFailed_NoOp_On_Null_Activity()
    {
        // No listener → null activity. MarkFailed must be a no-op, not throw NRE.
        var act = DeliberaActivitySource.StartActivity("none");
        act.Should().BeNull();
        var action = () => DeliberaTelemetry.MarkFailed(act, "x");
        action.Should().NotThrow();
    }

    [Fact]
    public void DeliberaTelemetry_MarkSucceeded_Sets_Ok_Status()
    {
        using var listener = CreateEnabledListener();
        ActivitySource.AddActivityListener(listener);

        using var activity = DeliberaTelemetry.StartDebate("d", "s", 1, 1);
        DeliberaTelemetry.MarkSucceeded(activity);

        activity!.Status.Should().Be(ActivityStatusCode.Ok);
        activity.GetTagItem(DeliberaTelemetryTags.Success).Should().Be("true");
    }

    [Fact]
    public void DeliberaMeter_RecordDebateCompleted_Emits_Histogram_And_Counter()
    {
        var recordedDurations = new ConcurrentBag<double>();
        var completedCount = 0;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DeliberaMeter.DefaultName
                && (instrument.Name == "delibera.debate.duration"
                    || instrument.Name == "delibera.debates.completed"))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "delibera.debate.duration")
                recordedDurations.Add(measurement);
        });
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "delibera.debates.completed")
                Interlocked.Increment(ref completedCount);
        });
        meterListener.Start();

        DeliberaTelemetry.RecordDebateCompleted(durationMs: 1234.5, strategyName: "Standard", success: true);

        // Allow the listener to dispatch callbacks on its own thread.
        Thread.Sleep(50);

        recordedDurations.Should().ContainSingle().Which.Should().BeApproximately(1234.5, 0.01);
        Volatile.Read(ref completedCount).Should().Be(1);
    }

    [Fact]
    public void DeliberaMeter_RecordTokens_No_Op_For_Zero_Or_Negative()
    {
        var count = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DeliberaMeter.DefaultName && instrument.Name == "delibera.tokens.total")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref count));
        meterListener.Start();

        DeliberaTelemetry.RecordTokens("m", "input", 0);
        DeliberaTelemetry.RecordTokens("m", "input", -5);

        Thread.Sleep(20);
        Volatile.Read(ref count).Should().Be(0);
    }

    [Fact]
    public void DeliberaMeter_RecordTokens_Emits_For_Positive_Count()
    {
        long total = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DeliberaMeter.DefaultName && instrument.Name == "delibera.tokens.total")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref total, measurement));
        meterListener.Start();

        DeliberaTelemetry.RecordTokens("Analyst", "input", 42);
        DeliberaTelemetry.RecordTokens("Analyst", "output", 100);

        Thread.Sleep(20);
        Interlocked.Read(ref total).Should().Be(142);
    }

    [Fact]
    public void CouncilBuilder_WithTelemetry_Enables_Telemetry_On_Executor()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithTelemetry()
            .Build();

        executor.IsTelemetryEnabled.Should().BeTrue();
    }

    [Fact]
    public void CouncilBuilder_WithoutTelemetry_Keeps_Telemetry_Disabled()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.IsTelemetryEnabled.Should().BeFalse();
    }

    [Fact]
    public void CouncilBuilder_WithTelemetry_Options_Disabled_Keeps_Executor_Disabled()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithTelemetry(new TelemetryOptions { Enabled = false })
            .Build();

        executor.IsTelemetryEnabled.Should().BeFalse();
    }

    [Fact]
    public void CouncilBuilder_WithTelemetry_Delegate_Overload_Applies_Configuration()
    {
        try
        {
            var provider = new FakeLLMProvider(reply: "ok");
            var executor = new CouncilBuilder()
                .AddMember("fake-model", provider, "Analyst")
                .WithStandardDebate()
                .WithSystemPrompt("sys")
                .WithUserPrompt("q")
                .WithMaxRounds(1)
                .WithTelemetry(o =>
                {
                    o.ActivitySourceName = "Test.ActivitySource";
                    o.MeterName = "Test.Meter";
                })
                .Build();

            executor.IsTelemetryEnabled.Should().BeTrue();
        }
        finally
        {
            DeliberaActivitySource.Configure(DeliberaActivitySource.DefaultName, "1.0.0");
            DeliberaMeter.Configure(DeliberaMeter.DefaultName, "1.0.0");
        }
    }

    [Fact]
    public async Task CouncilOptions_Telemetry_Enabled_Propagates_To_Executor_Via_ApplyOptions()
    {
        try
        {
            var provider = new FakeLLMProvider(reply: "ok");
            var opts = new CouncilOptions();
            opts.Telemetry.Enabled = true;
            opts.Telemetry.ActivitySourceName = "Test.FromOptions";

            var executor = new CouncilBuilder(opts)
                .AddMember("fake-model", provider, "Analyst")
                .WithStandardDebate()
                .WithUserPrompt("q")
                .WithMaxRounds(1)
                .Build();

            executor.IsTelemetryEnabled.Should().BeTrue();

            // Sanity: the executor still runs end-to-end.
            var result = await executor.ExecuteAsync();
            result.Should().NotBeNull();
            result.Rounds.Should().NotBeEmpty();
        }
        finally
        {
            DeliberaActivitySource.Configure(DeliberaActivitySource.DefaultName, "1.0.0");
            DeliberaMeter.Configure(DeliberaMeter.DefaultName, "1.0.0");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithTelemetry_Emits_Round_Duration_Metric()
    {
        var roundDurations = new ConcurrentBag<double>();

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DeliberaMeter.DefaultName
                && instrument.Name == "delibera.round.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, measurement, _, _) => roundDurations.Add(measurement));
        meterListener.Start();

        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithTelemetry()
            .Build();

        await executor.ExecuteAsync();

        // At least one round-duration measurement (round 1) should have been recorded.
        Thread.Sleep(30);
        roundDurations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithTelemetry_Records_DebateCompleted_Counter()
    {
        var completedCount = 0;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DeliberaMeter.DefaultName
                && instrument.Name == "delibera.debates.completed")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref completedCount));
        meterListener.Start();

        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithTelemetry()
            .Build();

        await executor.ExecuteAsync();

        Thread.Sleep(30);
        Volatile.Read(ref completedCount).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutTelemetry_Does_Not_Emit_Round_Metrics()
    {
        var roundDurations = new ConcurrentBag<double>();

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "delibera.round.duration")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((_, measurement, _, _) => roundDurations.Add(measurement));
        meterListener.Start();

        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build(); // No WithTelemetry()

        await executor.ExecuteAsync();

        Thread.Sleep(30);
        roundDurations.Should().BeEmpty();
    }

    [Fact]
    public void DeliberaActivityNames_Exposes_Canonical_Operation_Names()
    {
        // Static constants — sanity-check they're stable surface area.
        DeliberaActivityNames.CouncilExecute.Should().Be("delibera.council.execute");
        DeliberaActivityNames.CouncilRound.Should().Be("delibera.council.round");
        DeliberaActivityNames.MemberRespond.Should().Be("delibera.member.respond");
        DeliberaActivityNames.RagQuery.Should().Be("delibera.rag.query");
        DeliberaActivityNames.Compression.Should().Be("delibera.compression");
        DeliberaActivityNames.OperatorExecute.Should().Be("delibera.operator.execute");
        DeliberaActivityNames.ChairmanSynthesize.Should().Be("delibera.chairman.synthesize");
    }

    private static ActivityListener CreateEnabledListener() => new()
    {
        ShouldListenTo = src => src.Name == DeliberaActivitySource.DefaultName,
        SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
    };
}

/// <summary>
///    xUnit collection definition that serializes execution of every test in
///    <see cref="TelemetryTests"/>. The static <c>DeliberaActivitySource</c> /
///    <c>DeliberaMeter</c> singletons cannot be safely mutated in parallel.
/// </summary>
[CollectionDefinition("Telemetry", DisableParallelization = true)]
public class TelemetryCollection { }
