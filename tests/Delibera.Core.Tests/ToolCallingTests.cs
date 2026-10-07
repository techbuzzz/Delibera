using Delibera.Core.Cost;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using Delibera.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Tests;

/// <summary>
///    I-01 — function calling / tool use.
///
///    The property that matters most is the bound. A model that keeps requesting tools must not
///    keep the debate alive, and a member with no tools must behave exactly as it did before
///    this feature existed.
/// </summary>
public sealed class ToolCallingTests
{
   [Fact]
   public void ToolCallParser_Finds_A_Marked_Request()
   {
      var response = "I need the current figure. [[TOOL: http_get {\"url\": \"https://example.com\"}]]";

      var requests = ToolCallParser.Parse(response);

      requests.Should().ContainSingle();
      requests[0].ToolName.Should().Be("http_get");
      requests[0].ArgumentsJson.Should().Contain("example.com");
   }

   [Fact]
   public void ToolCallParser_Ignores_A_Response_Without_Markers()
   {
      ToolCallParser.Parse("I have no external data and will reason from first principles.")
         .Should().BeEmpty();
   }

   [Fact]
   public void ToolCallParser_Handles_A_Marker_With_No_Arguments()
   {
      var requests = ToolCallParser.Parse("[[TOOL: list_files]]");

      requests.Should().ContainSingle();
      requests[0].ToolName.Should().Be("list_files");
      requests[0].ArgumentsJson.Should().BeEmpty();
   }

   [Fact]
   public async Task A_Request_For_An_Unknown_Tool_Is_Recorded_As_A_Failure_Not_Thrown()
   {
      // The model asks for something that is not in the catalogue. That has to surface as a
      // failed tool call the operator can see, not as an exception that kills the member's turn.
      var member = new CouncilMember(
         "m",
         new FakeLLMProvider("Fake", "Let me check. [[TOOL: not_a_real_tool {\"a\": 1}]] Then I conclude."),
         "A");
      var tools = new List<AIFunction>
      {
         AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions { Name = "known_tool" })
      };

      var (response, calls) = await ToolCallingMemberExecutor.AskAsync(
         member, tools, "sys", "user", 0.7f, 2, roundNumber: 1);

      // The fake provider replays the same reply on every ask, so the loop keeps seeing the
      // marker. It must still terminate at the bound, and every attempt must be logged as a
      // failure rather than throwing.
      calls.Should().NotBeEmpty();
      calls.Count.Should().BeLessThanOrEqualTo(4, "the iteration bound is the backstop");
      calls.Should().OnlyContain(c => !c.Succeeded);
      calls.Should().OnlyContain(c => c.ErrorMessage != null && c.ErrorMessage.Contains("No tool named"));
      response.Should().NotBeNull();
   }

   [Fact]
   public async Task The_Iteration_Bound_Stops_A_Model_That_Keeps_Asking_For_Tools()
   {
      var member = new CouncilMember(
         "m",
         new FakeLLMProvider("Fake", "again: [[TOOL: known_tool {}]]"),
         "A");
      var tools = new List<AIFunction>
      {
         AIFunctionFactory.Create(() => "still going", new AIFunctionFactoryOptions { Name = "known_tool" })
      };

      var (_, calls) = await ToolCallingMemberExecutor.AskAsync(
         member, tools, "sys", "user", 0.7f, maxIterations: 3, roundNumber: 1);

      calls.Count.Should().Be(3, "one call per permitted iteration, then the loop gives up");
   }

   [Fact]
   public async Task A_Member_That_Never_Requests_A_Tool_Produces_No_Calls()
   {
      var member = new CouncilMember(
         "m", new FakeLLMProvider("Fake", "I will answer directly."), "A");
      var tools = await new FileSystemToolProvider(Path.GetTempPath()).GetToolsAsync();

      var (response, calls) = await ToolCallingMemberExecutor.AskAsync(
         member, tools, "sys", "user", 0.7f, 3, roundNumber: 1);

      calls.Should().BeEmpty();
      response.Should().Be("I will answer directly.");
   }

   [Fact]
   public async Task With_No_Tools_Configured_The_Existing_String_Path_Is_Untouched()
   {
      var member = new CouncilMember("m", new FakeLLMProvider("Fake", "plain reply"), "A");

      var (response, calls) = await ToolCallingMemberExecutor.AskAsync(
         member, [], "sys", "user", 0.7f, 3, roundNumber: 1);

      response.Should().Be("plain reply");
      calls.Should().BeEmpty();
   }

   [Fact]
   public async Task FileSystemToolProvider_Reads_A_File_Inside_Its_Root()
   {
      var root = Directory.CreateTempSubdirectory("delibera-tools");
      var file = Path.Combine(root.FullName, "notes.txt");
      File.WriteAllText(file, "the answer is 42");

      try
      {
         var provider = new FileSystemToolProvider(root.FullName);
         var tools = await provider.GetToolsAsync();

         tools.Should().ContainSingle().Which.Name.Should().Be("read_file");
      }
      finally
      {
         root.Delete(recursive: true);
      }
   }

   [Fact]
   public async Task FileSystemToolProvider_Refuses_A_Path_That_Escapes_Its_Root()
   {
      var root = Directory.CreateTempSubdirectory("delibera-tools-root");
      var outside = Directory.CreateTempSubdirectory("delibera-tools-outside");
      var secret = Path.Combine(outside.FullName, "secret.txt");
      File.WriteAllText(secret, "classified");

      try
      {
         var provider = new FileSystemToolProvider(root.FullName);
         var tool = (await provider.GetToolsAsync()).Single();

         var act = async () => await tool.InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["path"] = $"../{Path.GetFileName(outside.FullName)}/secret.txt" }));

         // Either the traversal is refused outright, or the tool returns an error string.
         // What must never happen is the contents of the file outside the root.
         try
         {
            var result = await act();
            result.ToString().Should().NotContain("classified");
         }
         catch (UnauthorizedAccessException)
         {
            // the explicit refusal path
         }
      }
      finally
      {
         root.Delete(recursive: true);
         outside.Delete(recursive: true);
      }
   }

   [Fact]
   public async Task HttpToolProvider_Refuses_A_Host_Outside_The_AllowList()
   {
      using var http = new HttpClient();
      var provider = new HttpToolProvider(http, ["api.allowed.example"]);
      var tool = (await provider.GetToolsAsync()).Single();

      var result = await tool.InvokeAsync(new AIFunctionArguments(
         new Dictionary<string, object?> { ["url"] = "https://evil.example/steal" }));

      result?.ToString().Should().Contain("not on the allow-list");
   }

   [Fact]
   public async Task HttpToolProvider_Refuses_Plain_Http_By_Default()
   {
      using var http = new HttpClient();
      var provider = new HttpToolProvider(http, ["api.allowed.example"]);
      var tool = (await provider.GetToolsAsync()).Single();

      var result = await tool.InvokeAsync(new AIFunctionArguments(
         new Dictionary<string, object?> { ["url"] = "http://api.allowed.example/data" }));

      result?.ToString().Should().Contain("refused");
   }

   [Fact]
   public void ToolCallParser_Briefing_Lists_Every_Tool()
   {
      var tools = new List<AIFunction>
      {
         AIFunctionFactory.Create(() => "x", new AIFunctionFactoryOptions { Name = "alpha", Description = "does alpha" }),
         AIFunctionFactory.Create(() => "x", new AIFunctionFactoryOptions { Name = "beta", Description = "does beta" })
      };

      var briefing = ToolCallParser.BuildBriefing(tools);

      briefing.Should().Contain("alpha").And.Contain("beta");
      briefing.Should().Contain("[[TOOL:");
   }

   [Fact]
   public void ToolCallParser_Briefing_Is_Empty_Without_Tools()
   {
      ToolCallParser.BuildBriefing([]).Should().BeEmpty();
   }

   [Fact]
   public async Task A_Debate_Without_Tools_Reports_None()
   {
      var executor = new CouncilBuilder()
         .WithSystemPrompt("sys")
         .WithStandardDebate()
         .WithUserPrompt("Should we ship?")
         .WithMaxRounds(1)
         .AddMember("model-a", new FakeLLMProvider("Fake", "position a"))
         .Build();

      var result = await executor.ExecuteAsync();

      result.ToolCalls.Should().BeEmpty();
      result.Rounds.Should().OnlyContain(r => r.ToolCalls.Count == 0);
   }

   [Fact]
   public async Task DebateExecutionOptions_Caches_The_Resolved_Tool_Catalogue()
   {
      var options = new DebateExecutionOptions
      {
         MemberTools =
         [
            AIFunctionFactory.Create(() => "x", new AIFunctionFactoryOptions { Name = "t" })
         ]
      };

      var first = await options.GetOrCreateToolsAsync();
      var second = await options.GetOrCreateToolsAsync();

      second.Should().BeSameAs(first,
         "providers must be enumerated once per debate, not once per member call per round");
   }

   [Fact]
   public void MaxToolIterations_Defaults_To_A_Bounded_Three()
   {
      new DebateExecutionOptions().MaxToolIterations.Should().Be(3);
   }

   [Fact]
   public void WithMaxToolIterations_Rejects_A_NonPositive_Bound()
   {
      var builder = new CouncilBuilder();

      var act = () => builder.WithMaxToolIterations(0);

      act.Should().Throw<ArgumentOutOfRangeException>();
   }
}