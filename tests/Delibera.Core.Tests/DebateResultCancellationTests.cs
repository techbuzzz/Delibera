using Delibera.Core.Models;
using FluentAssertions;

namespace Delibera.Core.Tests;

public class DebateResultCancellationTests
{
   private static DebateResult SampleResult() => new()
   {
      StrategyName = "Test",
      Context = new PromptContext { SystemPrompt = "sys", UserPrompt = "user" },
      Participants = ["a", "b"],
      Rounds =
      [
         new DebateRound
         {
            RoundNumber = 1,
            RoundName = "r1",
            Responses = new Dictionary<string, string> { ["a"] = "r-a", ["b"] = "r-b" }
         }
      ],
      FinalVerdict = "verdict",
      CompletedAt = DateTime.UtcNow
   };

   [Fact]
   public async Task SaveToMarkdownAsync_Honors_Pre_Canceled_Token()
   {
      var result = SampleResult();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => result.SaveToMarkdownAsync("ignored.md", cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task SaveStatisticsAsync_Honors_Pre_Canceled_Token()
   {
      var result = SampleResult();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => result.SaveStatisticsAsync("ignored.md", cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task SaveLogsAsync_Honors_Pre_Canceled_Token()
   {
      var result = SampleResult();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => result.SaveLogsAsync("ignored.md", cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task SaveToFileAsync_Honors_Pre_Canceled_Token()
   {
      var result = SampleResult();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => result.SaveToFileAsync("ignored.md", cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task SaveAllAsync_Honors_Pre_Canceled_Token()
   {
      var result = SampleResult();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => result.SaveAllAsync("./ignored_dir", ct: cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task SaveAllAsync_Creates_Files_When_Not_Canceled()
   {
      var result = SampleResult();
      var tempDir = Path.Combine(Path.GetTempPath(), "delibera_save_" + Guid.NewGuid().ToString("N"));

      try
      {
         var (r, s, l) = await result.SaveAllAsync(tempDir, "ct");

         File.Exists(r).Should().BeTrue();
         File.Exists(s).Should().BeTrue();
         File.Exists(l).Should().BeTrue();
      }
      finally
      {
         if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
      }
   }
}
