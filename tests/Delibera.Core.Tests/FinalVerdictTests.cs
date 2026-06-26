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
}
