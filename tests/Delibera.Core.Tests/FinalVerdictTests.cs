using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Tests;

public class FinalVerdictTests
{
   [Theory]
   [InlineData(1)]
   [InlineData(2)]
   [InlineData(3)]
   [InlineData(4)]
   public async Task StandardDebate_WithChairman_ProducesFinalVerdict_ForAnyMaxRounds(int maxRounds)
   {
      var provider = CreateProvider("standard-verdict");
      var member = new CouncilMember("model", provider);
      var chairman = new CouncilMember("chair", provider, "Chairman");

      var debate = new StandardDebate();
      var result = await debate.ExecuteAsync(
         [member],
         new PromptContext { UserPrompt = "What is 2+2?" },
         chairman,
         null,
         null,
         maxRounds: maxRounds);

      result.FinalVerdict.Should().NotBeNullOrWhiteSpace();
      result.FinalVerdict.Should().StartWith("standard-verdict");
   }

   [Fact]
   public async Task StandardDebate_WithoutChairman_LeavesFinalVerdictEmpty()
   {
      var provider = CreateProvider("standard-verdict");
      var member = new CouncilMember("model", provider);

      var debate = new StandardDebate();
      var result = await debate.ExecuteAsync(
         [member],
         new PromptContext { UserPrompt = "What is 2+2?" },
         null,
         null,
         null);

      result.FinalVerdict.Should().BeNullOrWhiteSpace();
   }

   [Fact]
   public async Task CritiqueDebate_WithChairman_ProducesFinalVerdict()
   {
      var provider = CreateProvider("critique-verdict");
      var member = new CouncilMember("model", provider);
      var chairman = new CouncilMember("chair", provider, "Chairman");

      var debate = new CritiqueDebate();
      var result = await debate.ExecuteAsync(
         [member],
         new PromptContext { UserPrompt = "Microservices vs monolith?" },
         chairman,
         null,
         null,
         maxRounds: 3);

      result.FinalVerdict.Should().NotBeNullOrWhiteSpace();
      result.FinalVerdict.Should().StartWith("critique-verdict");
   }

   [Fact]
   public async Task ConsensusDebate_WithChairman_ProducesFinalVerdict()
   {
      var provider = CreateProvider("consensus-verdict");
      var member = new CouncilMember("model", provider);
      var chairman = new CouncilMember("chair", provider, "Chairman");

      var debate = new ConsensusDebate();
      var result = await debate.ExecuteAsync(
         [member],
         new PromptContext { UserPrompt = "Best project structure?" },
         chairman,
         null,
         null,
         maxRounds: 2);

      result.FinalVerdict.Should().NotBeNullOrWhiteSpace();
      result.FinalVerdict.Should().StartWith("consensus-verdict");
   }

   [Fact]
   public async Task StandardDebate_RoundDuration_IsNonZero_WhenRoundWorkTakesTime()
   {
      var provider = CreateProvider("standard-verdict", delayMs: 50);
      var member = new CouncilMember("model", provider);
      var chairman = new CouncilMember("chair", provider, "Chairman");

      var debate = new StandardDebate();
      var result = await debate.ExecuteAsync(
         [member],
         new PromptContext { UserPrompt = "What is 2+2?" },
         chairman,
         null,
         null,
         maxRounds: 4);

      foreach (var round in result.Rounds)
         round.Duration.Should().BeGreaterThan(TimeSpan.Zero, $"Round {round.RoundNumber} should have non-zero duration");
   }

    private static ILLMProvider CreateProvider(string reply, int delayMs = 0)
    {
       var client = new FakeChatClient(reply: reply, delayMs: delayMs);
       return new ChatClientLLMProvider(client, ownsClient: true);
    }

    /// <summary>
    ///    Regression test for the [CHAIRMAN ERROR: The operation was canceled.]
    ///    bug: when the wall-clock budget CT fires after the last deliberation
    ///    round but before the Chairman verdict synthesis, the StandardDebate's
    ///    FinalizeAsync must still produce a real verdict. The fix switches the
    ///    verdict call to CancellationToken.None when the budget CT is the only
    ///    signal that fired.
    /// </summary>
    [Fact]
    public async Task StandardDebate_ProducesRealVerdict_WhenBudgetCtFiresBeforeSynthesis()
    {
       // Provider that ignores the CT on the very first call (Chairman.OpenDebateAsync,
       // which runs outside the rounds) and throws OperationCanceledException on every
       // subsequent call when the CT is cancelled. This emulates the real OllamaSharp
       // streaming behaviour that respects the CT — and matches the production failure
       // where the wall-clock budget fires mid-debate, the rounds complete with
       // "[ERROR: …]" responses (caught by CollectResponsesAsync), and FinalizeAsync
       // then observes ct.IsCancellationRequested == true.
       const string verdict = "standard-verdict";
       var provider = new CancellationAwareLLMProvider(verdict);
       var member = new CouncilMember("model", provider);
       var chairman = new CouncilMember("chair", provider, "Chairman");

       using var budgetCts = new CancellationTokenSource();
       budgetCts.Cancel();

       var debate = new StandardDebate();
       // maxRounds=1 so only the Initial Positions round runs before FinalizeAsync.
       var result = await debate.ExecuteAsync(
          [member],
          new PromptContext { UserPrompt = "What is 2+2?" },
          chairman,
          null,
          null,
          maxRounds: 1,
          ct: budgetCts.Token);

       result.FinalVerdict.Should().NotBeNullOrWhiteSpace();
       result.FinalVerdict.Should().StartWith(verdict);
       result.FinalVerdict.Should().NotContain("CHAIRMAN ERROR");
    }

    /// <summary>
    ///    Same regression as above but for the CritiqueDebate's [JUDGE ERROR: …]
    ///    placeholder.
    /// </summary>
    [Fact]
    public async Task CritiqueDebate_ProducesRealVerdict_WhenBudgetCtFiresBeforeSynthesis()
    {
       const string verdict = "critique-verdict";
       var provider = new CancellationAwareLLMProvider(verdict);
       var member = new CouncilMember("model", provider);
       var chairman = new CouncilMember("chair", provider, "Chairman");

       using var budgetCts = new CancellationTokenSource();
       budgetCts.Cancel();

       var debate = new CritiqueDebate();
       var result = await debate.ExecuteAsync(
          [member],
          new PromptContext { UserPrompt = "Microservices vs monolith?" },
          chairman,
          null,
          null,
          maxRounds: 1,
          ct: budgetCts.Token);

       result.FinalVerdict.Should().NotBeNullOrWhiteSpace();
       result.FinalVerdict.Should().StartWith(verdict);
       result.FinalVerdict.Should().NotContain("JUDGE ERROR");
    }

    /// <summary>
    ///    Same regression as above but for the ConsensusDebate's
    ///    [FACILITATOR ERROR: …] placeholder.
    /// </summary>
    [Fact]
    public async Task ConsensusDebate_ProducesRealVerdict_WhenBudgetCtFiresBeforeSynthesis()
    {
       const string verdict = "consensus-verdict";
       var provider = new CancellationAwareLLMProvider(verdict);
       var member = new CouncilMember("model", provider);
       var chairman = new CouncilMember("chair", provider, "Chairman");

       using var budgetCts = new CancellationTokenSource();
       budgetCts.Cancel();

       var debate = new ConsensusDebate();
       var result = await debate.ExecuteAsync(
          [member],
          new PromptContext { UserPrompt = "Best project structure?" },
          chairman,
          null,
          null,
          maxRounds: 1,
          ct: budgetCts.Token);

       result.FinalVerdict.Should().NotBeNullOrWhiteSpace();
       result.FinalVerdict.Should().StartWith(verdict);
       result.FinalVerdict.Should().NotContain("FACILITATOR ERROR");
    }
}

/// <summary>
///    An <see cref="ILLMProvider"/> that emulates the real OllamaSharp CT
///    behaviour: the very first <see cref="ChatAsync"/> call (the Chairman's
///    OpenDebateAsync, which runs before any round) ignores the cancellation
///    token so the opening statement always succeeds; every subsequent call
///    throws <see cref="OperationCanceledException"/> when the token is
///    already cancelled. This lets tests reproduce the production failure
///    where a wall-clock budget fires mid-debate — the rounds complete (with
///    "[ERROR: …]" responses, caught by CollectResponsesAsync) and
///    FinalizeAsync then sees <c>ct.IsCancellationRequested == true</c>.
/// </summary>
file sealed class CancellationAwareLLMProvider(string reply) : ILLMProvider
{
   private int _callCount;

   public string ProviderName => "CancellationAware";

   public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

   public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<string>>(["ca-model"]);

   public Task<string> ChatAsync(
      string model,
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      CancellationToken ct = default)
   {
      var n = Interlocked.Increment(ref _callCount);
      // First call (Chairman.OpenDebateAsync) always succeeds — it runs before
      // the rounds and is not guarded by the debate's try/catch.
      if (n == 1) return Task.FromResult(reply);
      // Every subsequent call respects the CT, matching OllamaSharp streaming.
      if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
      return Task.FromResult(reply);
   }

   public void Dispose() { }
}
