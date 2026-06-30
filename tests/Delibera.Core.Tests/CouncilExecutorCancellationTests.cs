using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

public class CouncilExecutorCancellationTests
{
   private static ICouncilExecutor BuildMinimalExecutor(
      FakeLLMProvider provider,
      int maxRounds = 1)
   {
      // CouncilBuilder accepts a string for the user prompt, etc.
      // We use 1 round, 1 member, no chairman (Chairman is optional in v3.1).
      // The caller-supplied FakeLLMProvider is what the executor will actually use,
      // so call-count assertions on the provider reflect real LLM activity.
      return new CouncilBuilder()
         .AddMember("fake-model", provider, "Analyst")
         .WithStandardDebate()
         .WithSystemPrompt("sys")
         .WithUserPrompt("user-question")
         .WithMaxRounds(maxRounds)
         .WithTemperature(0.3f)
         .Build();
   }

   [Fact]
   public async Task ExecuteAsync_NoCancel_Runs_And_Produces_Result()
   {
      var fake = new FakeLLMProvider(reply: "ok");
      var executor = BuildMinimalExecutor(fake);

      var result = await executor.ExecuteAsync();

      result.Should().NotBeNull();
      result.Rounds.Should().NotBeEmpty();
      // Sanity: at least one member produced a response in at least one round.
      result.Rounds.Should().Contain(r => r.Responses.Count > 0);
   }

   [Fact]
   public async Task ExecuteAsync_WithOutputPath_ForwardsCancellationToFinalSave()
   {
      // Verifies Phase 1.4 fix: CouncilExecutor.ExecuteAsync now forwards the
      // caller's CT to result.SaveToFileAsync. We use a pre-cancelled token —
      // since the debate path itself is OCE-resilient (CollectResponsesAsync
      // catches LLM errors), the debate can complete; the SaveToFileAsync call
      // will then observe the pre-cancelled token and throw.
      var tempDir = Path.Combine(Path.GetTempPath(), "delibera_exec_save_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      try
      {
         var outputPath = Path.Combine(tempDir, "result.md");
         var provider = new FakeLLMProvider(reply: "ok");
         var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("user-question")
            .WithMaxRounds(1)
            .WithTemperature(0.3f)
            .SaveResultTo(outputPath)
            .Build();

         using var cts = new CancellationTokenSource();
         cts.Cancel();

         // The debate will run quickly (LLM is fast). When it reaches the
         // SaveToFileAsync call, the pre-cancelled token will be observed and
         // an OperationCanceledException will be thrown.
         var act = () => executor.ExecuteAsync(cts.Token);
         await act.Should().ThrowAsync<OperationCanceledException>();
      }
      finally
      {
         if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
      }
   }
}

