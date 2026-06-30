using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Extensions;
using Delibera.Core.Interfaces;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

public class CouncilExecutorLifetimeExtensionsTests
{
   private sealed class TestLifetime(CancellationToken token) : IAppStoppingToken
   {
      public CancellationToken ApplicationStopping => token;
   }

   private static ICouncilExecutor BuildExecutor(FakeLLMProvider provider)
   {
      // Caller-supplied FakeLLMProvider is used directly so call-count assertions
      // on the provider reflect real LLM activity.
      return new CouncilBuilder()
         .AddMember("fake-model", provider, "Analyst")
         .WithStandardDebate()
         .WithSystemPrompt("sys")
         .WithUserPrompt("user-question")
         .WithMaxRounds(1)
         .WithTemperature(0.3f)
         .Build();
   }

   [Fact]
   public async Task ExecuteAsync_WithLifetime_RespectsCallerToken_OnSavePath()
   {
      // Same caveat as the executor test: the debate rounds absorb LLM OCE.
      // We exercise the post-debate SaveToFileAsync path by configuring an
      // output path and pre-cancelling the token — the SaveToFileAsync call
      // will observe the pre-cancelled token and throw.
      var tempDir = Path.Combine(Path.GetTempPath(), "delibera_lifetime_save_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      try
      {
         var outputPath = Path.Combine(tempDir, "result.md");
         var fake = new FakeLLMProvider(reply: "ok");
         var executor = new CouncilBuilder()
            .AddMember("fake-model", fake, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("user-question")
            .WithMaxRounds(1)
            .WithTemperature(0.3f)
            .SaveResultTo(outputPath)
            .Build();

         using var lifetimeCts = new CancellationTokenSource();
         var lifetime = new TestLifetime(lifetimeCts.Token);

         using var callerCts = new CancellationTokenSource();
         callerCts.Cancel();

         var act = () => executor.ExecuteAsync(lifetime, callerCts.Token);
         await act.Should().ThrowAsync<OperationCanceledException>();
      }
      finally
      {
         if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
      }
   }

   [Fact]
   public async Task ExecuteAsync_WithLifetime_RespectsApplicationStopping_OnSavePath()
   {
      // Same caveat as before: debate rounds absorb LLM OCE. Exercise the
      // post-debate SaveToFileAsync path by configuring an output path and
      // pre-cancelling the lifetime token — the SaveToFileAsync call will
      // observe the pre-cancelled token via the linked CTS and throw.
      var tempDir = Path.Combine(Path.GetTempPath(), "delibera_lifetime_appstop_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      try
      {
         var outputPath = Path.Combine(tempDir, "result.md");
         var fake = new FakeLLMProvider(reply: "ok");
         var executor = new CouncilBuilder()
            .AddMember("fake-model", fake, "Analyst")
            .WithStandardDebate()
            .WithSystemPrompt("sys")
            .WithUserPrompt("user-question")
            .WithMaxRounds(1)
            .WithTemperature(0.3f)
            .SaveResultTo(outputPath)
            .Build();

         using var lifetimeCts = new CancellationTokenSource();
         lifetimeCts.Cancel();
         var lifetime = new TestLifetime(lifetimeCts.Token);

         var act = () => executor.ExecuteAsync(lifetime);
         await act.Should().ThrowAsync<OperationCanceledException>();
      }
      finally
      {
         if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
      }
   }

   [Fact]
   public async Task ExecuteAsync_WithLifetime_RunsToCompletion_When_NeitherCanceled()
   {
      var fake = new FakeLLMProvider(reply: "ok");
      var executor = BuildExecutor(fake);
      using var lifetimeCts = new CancellationTokenSource();
      var lifetime = new TestLifetime(lifetimeCts.Token);

      var result = await executor.ExecuteAsync(lifetime);

      result.Should().NotBeNull();
      result.Rounds.Should().NotBeEmpty();
   }

   [Fact]
   public async Task ExecuteAsync_WithLifetime_NullExecutor_Throws()
   {
      ICouncilExecutor? executor = null;
      using var lifetimeCts = new CancellationTokenSource();
      var lifetime = new TestLifetime(lifetimeCts.Token);

      var act = () => executor!.ExecuteAsync(lifetime);
      await act.Should().ThrowAsync<ArgumentNullException>();
   }

   [Fact]
   public async Task ExecuteAsync_WithLifetime_NullLifetime_Throws()
   {
      var fake = new FakeLLMProvider(reply: "ok");
      var executor = BuildExecutor(fake);

      var act = () => executor.ExecuteAsync(lifetime: null!);
      await act.Should().ThrowAsync<ArgumentNullException>();
   }
}
