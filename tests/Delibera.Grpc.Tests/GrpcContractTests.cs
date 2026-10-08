using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Grpc.Services;
using Delibera.Grpc.V1;
using FluentAssertions;

namespace Delibera.Grpc.Tests;

/// <summary>
///    I-11 — the gRPC contract.
///    <para>
///    These are contract tests, not transport tests: they pin the mappings that silently drift.
///    The enum test is the one that matters most. The REST API serialises
///    <c>DebateOrchestrationStatus</c> as strings and the proto pins numeric values, so nothing
///    in either build stops the two from disagreeing — only a test that states the pairing does.
///    </para>
/// </summary>
public sealed class GrpcContractTests
{
   [Theory]
   [InlineData(DebateOrchestrationStatus.Running, DebateStatus.Running)]
   [InlineData(DebateOrchestrationStatus.Completed, DebateStatus.Completed)]
   [InlineData(DebateOrchestrationStatus.Cancelled, DebateStatus.Cancelled)]
   [InlineData(DebateOrchestrationStatus.Failed, DebateStatus.Failed)]
   public void DebateStatus_Maps_To_The_State_The_Rest_Api_Names(
      DebateOrchestrationStatus frameworkStatus,
      DebateStatus expected)
   {
      DebateMapper.DebateStatusValue(frameworkStatus).Should().Be(expected);
   }

   [Fact]
   public void DebateStatus_Proto_Values_Are_Pinned_Not_Implicit()
   {
      // Pinning these in the proto and asserting them here means a well-meaning reordering of the
      // enum cannot repoint every deployed gRPC client at the wrong state without failing a build.
      ((int)DebateStatus.Unspecified).Should().Be(0);
      ((int)DebateStatus.Pending).Should().Be(1);
      ((int)DebateStatus.Running).Should().Be(2);
      ((int)DebateStatus.Completed).Should().Be(3);
      ((int)DebateStatus.Cancelled).Should().Be(4);
      ((int)DebateStatus.Failed).Should().Be(5);
   }

   [Fact]
   public void ToRound_Carries_Every_Member_Response()
   {
      var round = new DebateRound
      {
         RoundNumber = 2,
         RoundName = "Critique",
         Description = "attack the previous positions",
         Responses = new Dictionary<string, string>
         {
            ["Expert: alice"] = "the caching layer is the bottleneck",
            ["Expert: bob"] = "disagree; the prompt size is"
         }
      };

      var dto = DebateMapper.ToRound("debate-1", round);

      dto.DebateId.Should().Be("debate-1", "a client multiplexing streams needs to know which debate a round belongs to");
      dto.RoundNumber.Should().Be(2);
      dto.RoundName.Should().Be("Critique");
      dto.Description.Should().Be("attack the previous positions");
      dto.Responses.Should().HaveCount(2);
      dto.Responses["Expert: alice"].Should().Be("the caching layer is the bottleneck");
      dto.Responses["Expert: bob"].Should().Be("disagree; the prompt size is");
   }

   [Fact]
   public void ToRound_Tolerates_An_Empty_Round_Rather_Than_Throwing()
   {
      var round = new DebateRound
      {
         RoundNumber = 1,
         RoundName = "Opening",
         Responses = new Dictionary<string, string>()
      };

      var act = () => DebateMapper.ToRound("d", round);

      act.Should().NotThrow();
      act().Responses.Should().BeEmpty();
   }

   [Fact]
   public void ToTimestamp_Treats_The_Default_Instant_As_Unset()
   {
      DebateMapper.ToTimestamp(default(DateTime)).Should().BeNull(
         "a missing completion time is not the same fact as 'completed at 1970-01-01'");
   }

   [Fact]
   public void ToTimestamp_Preserves_A_Real_Instant()
   {
      var moment = new DateTimeOffset(2026, 10, 7, 12, 34, 56, TimeSpan.Zero);

      var timestamp = DebateMapper.ToTimestamp(moment);

      timestamp.Should().NotBeNull();
      timestamp!.ToDateTimeOffset().Should().Be(moment);
   }

   [Fact]
   public void StreamEvent_Carries_A_Oneof_Kind_Rather_Than_Ambiguous_Fields()
   {
      var roundEvent = new DebateStreamEvent
      {
         Round = new RoundDto { DebateId = "d", RoundNumber = 1 }
      };

      roundEvent.KindCase.Should().Be(DebateStreamEvent.KindOneofCase.Round);
      roundEvent.Started.Should().BeNull();
      roundEvent.Completed.Should().BeNull();
      roundEvent.Error.Should().BeNull();
   }
}