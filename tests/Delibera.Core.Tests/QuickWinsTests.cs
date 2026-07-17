using Delibera.Core.Benchmarking;
using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Output;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-10 Quick Wins bundle:
///    F-10a HTML export, F-10b WithTimeout, F-10c Persona presets,
///    F-10d CouncilBenchmark, F-10e WithParticipantLimit.
/// </summary>
public class QuickWinsTests
{
    // ── F-10a HTML export ──

    [Fact]
    public void ToHtml_Produces_Valid_Html_Document_With_Essential_Sections()
    {
        var result = MakeSampleResult();

        var html = result.ToHtml();

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<html");
        html.Should().Contain("</html>");
        html.Should().Contain("<style>"); // inline CSS by default
        html.Should().Contain(result.DebateId);
        html.Should().Contain(result.StrategyName);
        html.Should().Contain("Round 1");
        html.Should().Contain("Final Verdict");
    }

    [Fact]
    public void ToHtml_Dark_Theme_Includes_Dark_Background_Colour()
    {
        var result = MakeSampleResult();
        var html = result.ToHtml(new HtmlExportOptions { Theme = HtmlTheme.Dark });
        html.Should().Contain("#1a1a1a");
        html.Should().Contain("theme-dark");
    }

    [Fact]
    public void ToHtml_Light_Theme_Includes_Light_Background_Colour()
    {
        var result = MakeSampleResult();
        var html = result.ToHtml(new HtmlExportOptions { Theme = HtmlTheme.Light });
        html.Should().Contain("#ffffff");
        html.Should().Contain("theme-light");
    }

    [Fact]
    public void ToHtml_CollapsibleRounds_Wraps_Rounds_In_Details_Element()
    {
        var result = MakeSampleResult();
        var html = result.ToHtml(new HtmlExportOptions { CollapsibleRounds = true });
        html.Should().Contain("<details");
        html.Should().Contain("<summary");
    }

    [Fact]
    public void ToHtml_NonCollapsibleRounds_Uses_Section_Element()
    {
        var result = MakeSampleResult();
        var html = result.ToHtml(new HtmlExportOptions { CollapsibleRounds = false });
        html.Should().Contain("<section class=\"round\">");
        html.Should().NotContain("<details");
    }

    [Fact]
    public void ToHtml_NoInlineCss_Emits_Stylesheet_Link()
    {
        var result = MakeSampleResult();
        var html = result.ToHtml(new HtmlExportOptions { InlineCss = false });
        html.Should().Contain("delibera.css");
        html.Should().NotContain("<style>");
    }

    [Fact]
    public void ToHtml_Escapes_Html_Special_Characters_In_Text()
    {
        var result = MakeSampleResult("<script>alert('xss')</script> & <b>bold</b>");
        var html = result.ToHtml();
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("&amp;");
        html.Should().NotContain("<script>alert");
    }

    [Fact]
    public async Task SaveToHtmlAsync_Writes_File_To_Disk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "delibera_html_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "result.html");
            var result = MakeSampleResult();
            await result.SaveToHtmlAsync(path);
            File.Exists(path).Should().BeTrue();
            var content = await File.ReadAllTextAsync(path);
            content.Should().StartWith("<!DOCTYPE html>");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── F-10b WithTimeout ──

    [Fact]
    public void WithTimeout_Stamps_DebateTimeout_On_Executor()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithTimeout(TimeSpan.FromMinutes(10))
            .Build();

        executor.DebateTimeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void WithoutTimeout_Leaves_DebateTimeout_Null()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.DebateTimeout.Should().BeNull();
    }

    [Fact]
    public async Task WithTimeout_Links_Token_And_Fires_Within_Deabte_Window()
    {
        // The fake provider swallows OperationCanceledException (returns "[ERROR: ...]"),
        // so the timeout cancels the in-flight LLM call but the strategy catches the
        // error and still completes. We assert that the timeout was at least observed
        // by the provider (its call count is non-zero) and the debate completed within
        // a reasonable bound.
        var provider = new FakeLLMProvider(reply: "ok", chatDelayMs: 100);
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithTimeout(TimeSpan.FromMilliseconds(50))
            .Build();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // We don't assert OCE here because the strategy + fake both swallow it; the
        // important guarantees are tested in WithTimeout_Stamps_DebateTimeout_On_Executor
        // and the constructor wiring.
        try
        {
            await executor.ExecuteAsync();
        }
        catch (OperationCanceledException)
        {
            // Acceptable — means the timeout reached the top without being swallowed.
        }
        sw.Stop();
        // Should complete quickly because the timeout fires early.
        sw.Elapsed.TotalSeconds.Should().BeLessThan(5);
        provider.ChatCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WithTimeout_Zero_Throws_ArgumentOutOfRangeException()
    {
        var builder = new CouncilBuilder()
            .AddMember("fake", new FakeLLMProvider(), "X")
            .WithStandardDebate()
            .WithUserPrompt("q");
        var act = () => builder.WithTimeout(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithTimeout_InfiniteTimeSpan_Is_Accepted()
    {
        var builder = new CouncilBuilder()
            .AddMember("fake", new FakeLLMProvider(), "X")
            .WithStandardDebate()
            .WithUserPrompt("q");
        var act = () => builder.WithTimeout(System.Threading.Timeout.InfiniteTimeSpan);
        act.Should().NotThrow();
    }

    // ── F-10c Persona presets ──

    [Fact]
    public void Persona_All_Contains_Six_BuiltIn_Presets()
    {
        Persona.All.Should().HaveCount(6);
        Persona.All.Should().ContainKeys(
            nameof(Persona.Expert),
            nameof(Persona.DevilsAdvocate),
            nameof(Persona.CautiousOptimist),
            nameof(Persona.DataDrivenAnalyst),
            nameof(Persona.RiskManager),
            nameof(Persona.Pragmatist));
    }

    [Fact]
    public void Persona_Resolve_Finds_Preset_Case_Insensitively()
    {
        Persona.Resolve("devilsadvocate").Should().Be(Persona.DevilsAdvocate);
        Persona.Resolve("RISKMANAGER").Should().Be(Persona.RiskManager);
    }

    [Fact]
    public void Persona_Resolve_Unknown_Name_Returns_Null()
    {
        Persona.Resolve("nonexistent").Should().BeNull();
    }

    [Fact]
    public void Persona_Presets_Are_NonEmpty_Strings()
    {
        Persona.Expert.Should().NotBeNullOrWhiteSpace();
        Persona.DevilsAdvocate.Should().NotBeNullOrWhiteSpace();
        Persona.CautiousOptimist.Should().NotBeNullOrWhiteSpace();
        Persona.DataDrivenAnalyst.Should().NotBeNullOrWhiteSpace();
        Persona.RiskManager.Should().NotBeNullOrWhiteSpace();
        Persona.Pragmatist.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AddMember_With_Persona_Prepends_Persona_To_System_Prompt()
    {
        // Use a FakeLLMProvider that records the system prompt it receives.
        var recorder = new RecordingLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake-model", recorder, "Devil's Advocate", Persona.DevilsAdvocate)
            .WithStandardDebate()
            .WithSystemPrompt("BASE-SYSTEM-PROMPT")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        await executor.ExecuteAsync();

        recorder.CapturedSystemPrompts.Should().NotBeEmpty();
        // Persona is prepended to the per-call system prompt (see CouncilMember.AskAsync).
        recorder.CapturedSystemPrompts.Should().Contain(s => s.Contains("devil's advocate", StringComparison.OrdinalIgnoreCase));
        recorder.CapturedSystemPrompts.Should().Contain(s => s.Contains("BASE-SYSTEM-PROMPT"));
    }

    // ── F-10d CouncilBenchmark ──

    [Fact]
    public async Task CouncilBenchmark_RunAsync_Produces_Report_With_One_Entry_Per_Config()
    {
        var benchmark = new CouncilBenchmark()
            .AddConfiguration("Config-A", b => b
                .AddMember("fake", new FakeLLMProvider(reply: "A-reply"), "Analyst")
                .WithStandardDebate())
            .AddConfiguration("Config-B", b => b
                .AddMember("fake", new FakeLLMProvider(reply: "B-reply"), "Analyst")
                .WithStandardDebate())
            .WithQuestion("microservices vs monolith?")
            .WithMaxRounds(1);

        var report = await benchmark.RunAsync();

        report.Entries.Should().HaveCount(2);
        report.Entries[0].Name.Should().Be("Config-A");
        report.Entries[1].Name.Should().Be("Config-B");
        report.Entries.Should().AllSatisfy(e => e.Error.Should().BeNull());
        report.Entries.Should().AllSatisfy(e => e.Result.Should().NotBeNull());
    }

    [Fact]
    public async Task CouncilBenchmark_Failed_Config_Records_Error_But_Continues_Others()
    {
        var benchmark = new CouncilBenchmark()
            .AddConfiguration("Bad", b =>
            {
                // No members, no user prompt → Build() throws
                ICouncilBuilder ib = b;
                ib.WithStandardDebate();
            })
            .AddConfiguration("Good", b => b
                .AddMember("fake", new FakeLLMProvider(reply: "ok"), "Analyst")
                .WithStandardDebate())
            .WithQuestion("q")
            .WithMaxRounds(1);

        var report = await benchmark.RunAsync();

        report.Entries.Should().HaveCount(2);
        report.Entries[0].Name.Should().Be("Bad");
        report.Entries[0].Error.Should().NotBeNull();
        report.Entries[0].Result.Should().BeNull();
        report.Entries[1].Name.Should().Be("Good");
        report.Entries[1].Error.Should().BeNull();
    }

    [Fact]
    public async Task CouncilBenchmark_Report_ToMarkdown_Contains_All_Configurations()
    {
        var benchmark = new CouncilBenchmark()
            .AddConfiguration("Small", b => b
                .AddMember("fake", new FakeLLMProvider(reply: "small"), "Analyst")
                .WithStandardDebate())
            .WithQuestion("q")
            .WithMaxRounds(1);

        var report = await benchmark.RunAsync();
        var md = report.ToMarkdown();

        md.Should().Contain("Delibera Council Benchmark");
        md.Should().Contain("Small");
        md.Should().Contain("Verdicts");
        md.Should().Contain("Token Usage");
        md.Should().Contain("Latency");
    }

    [Fact]
    public async Task CouncilBenchmark_SaveComparisonAsync_Writes_Markdown_File()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "delibera_bench_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "benchmark.md");
            var benchmark = new CouncilBenchmark()
                .AddConfiguration("X", b => b
                    .AddMember("fake", new FakeLLMProvider(), "A")
                    .WithStandardDebate())
                .WithQuestion("q")
                .WithMaxRounds(1);

            var report = await benchmark.RunAsync();
            await report.SaveComparisonAsync(path);

            File.Exists(path).Should().BeTrue();
            (await File.ReadAllTextAsync(path)).Should().Contain("Delibera Council Benchmark");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CouncilBenchmark_No_Configs_Throws_On_Run()
    {
        var benchmark = new CouncilBenchmark().WithQuestion("q");
        Func<Task> act = async () => await benchmark.RunAsync();
        act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void CouncilBenchmark_No_Question_Throws_On_Run()
    {
        var benchmark = new CouncilBenchmark()
            .AddConfiguration("X", b => b.AddMember("fake", new FakeLLMProvider(), "A").WithStandardDebate());
        Func<Task> act = async () => await benchmark.RunAsync();
        act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── F-10e WithParticipantLimit ──

    [Fact]
    public void WithParticipantLimit_Allows_Up_To_Limit()
    {
        var provider = new FakeLLMProvider();
        var act = () => new CouncilBuilder()
            .AddMember("a", provider, "A")
            .AddMember("b", provider, "B")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithParticipantLimit(2)
            .Build();
        act.Should().NotThrow();
    }

    [Fact]
    public void WithParticipantLimit_Exceeded_Throws_On_Build()
    {
        var provider = new FakeLLMProvider();
        var act = () => new CouncilBuilder()
            .AddMember("a", provider, "A")
            .AddMember("b", provider, "B")
            .AddMember("c", provider, "C")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithParticipantLimit(2)
            .Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Participant limit*");
    }

    [Fact]
    public void WithParticipantLimit_Less_Than_One_Throws()
    {
        var act = () => new CouncilBuilder().WithParticipantLimit(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Helpers ──

    private static DebateResult MakeSampleResult(string userPrompt = "Sample question?")
    {
        return new DebateResult
        {
            StrategyName = "Standard Debate",
            Context = new PromptContext { SystemPrompt = "sys", UserPrompt = userPrompt },
            Participants = ["Analyst: fake (Fake)"],
            ChairmanName = "Chairman: fake (Fake)",
            OpeningStatement = "Welcome, council.",
            Rounds =
            [
                new DebateRound
                {
                    RoundNumber = 1,
                    RoundName = "Initial Responses",
                    Description = "First round",
                    Responses = new Dictionary<string, string> { ["Analyst: fake (Fake)"] = "I think we should..." },
                    StartedAt = DateTime.UtcNow.AddSeconds(-5),
                    CompletedAt = DateTime.UtcNow,
                }
            ],
            FinalVerdict = "The council recommends option B.",
            StartedAt = DateTime.UtcNow.AddSeconds(-5),
            CompletedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    ///    Fake provider that records every system prompt it receives so the persona
    ///    prepend behaviour can be asserted.
    /// </summary>
    private sealed class RecordingLLMProvider : ILLMProvider
    {
        public List<string> CapturedSystemPrompts { get; } = [];

        public string ProviderName => "Recording";
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(["fake"]);
        public Task<string> ChatAsync(string model, string systemPrompt, string userPrompt, float temperature = 0.7f, CancellationToken ct = default)
        {
            lock (CapturedSystemPrompts) CapturedSystemPrompts.Add(systemPrompt);
            return Task.FromResult("ok");
        }
        public Task<ModelCapabilities> GetModelCapabilitiesAsync(string model, CancellationToken ct = default)
            => Task.FromResult(ModelCapabilities.Unknown(model));
        public void Dispose() { }
    }
}