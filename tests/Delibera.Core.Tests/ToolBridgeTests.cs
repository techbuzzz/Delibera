using Delibera.Core.Cost;
using Delibera.Core.Council;
using Delibera.Core.Extensions;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using Delibera.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Tests;

/// <summary>
///    The tool bridge between Microsoft.Extensions.AI and Delibera's string-only providers.
/// </summary>
/// <remarks>
///    The defect these pin: <c>LLMProviderChatClient</c> ignored <c>ChatOptions.Tools</c> and
///    flattened every message through <c>message.Text</c>. Function-call content has no
///    <c>Text</c>, so the adapter could neither surface a tool request nor carry a tool result, and
///    <c>FunctionInvokingChatClient</c> — which reacts only to a <c>FunctionCallContent</c> from the
///    inner client — never started. The caller got an ordinary answer and no error explaining why
///    nothing had been invoked.
/// </remarks>
public sealed class ToolBridgeTests
{
   [Fact]
   public async Task The_Adapter_Renders_Configured_Tools_Into_The_Prompt()
   {
      var provider = new RecordingProvider();
      var client = provider.AsChatClient();

      var tools = new List<AITool>
      {
         AIFunctionFactory.Create(
            () => "42",
            new AIFunctionFactoryOptions { Name = "calculator", Description = "adds numbers" })
      };

      await client.GetResponseAsync(
         [new ChatMessage(ChatRole.User, "add something")],
         new ChatOptions { Tools = tools });

      // The provider-facing prompt must contain the tool catalogue, otherwise the model has no way
      // to know a tool exists. Before the fix the adapter discarded options.Tools outright.
      provider.LastSystemPrompt.Should().Contain("calculator");
      provider.LastSystemPrompt.Should().Contain("[[TOOL:");
   }

   [Fact]
   public async Task The_Adapter_Turns_A_Marker_Into_A_Real_FunctionCall()
   {
      var provider = new FakeLLMProvider("Fake", "Working on it. [[TOOL: calculator {\"a\": 1}]]");
      var client = provider.AsChatClient();

      var tools = new List<AITool>
      {
         AIFunctionFactory.Create(() => "42", new AIFunctionFactoryOptions { Name = "calculator" })
      };

      var response = await client.GetResponseAsync(
         [new ChatMessage(ChatRole.User, "add something")],
         new ChatOptions { Tools = tools });

      var calls = response.Messages
         .SelectMany(m => m.Contents)
         .OfType<FunctionCallContent>()
         .ToList();

      calls.Should().ContainSingle("this is the signal FunctionInvokingChatClient acts on");
      calls[0].Name.Should().Be("calculator");
      calls[0].CallId.Should().NotBeNullOrWhiteSpace("a result has to be pairable back to this call");
   }

   [Fact]
   public async Task The_Adapter_Strips_The_Marker_From_The_Visible_Text()
   {
      var provider = new FakeLLMProvider("Fake", "Here is my reasoning. [[TOOL: calculator {}]]");
      var client = provider.AsChatClient();

      var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "go")],
         new ChatOptions
         {
            Tools = [AIFunctionFactory.Create(() => "42", new AIFunctionFactoryOptions { Name = "calculator" })]
         });

      response.Text.Should().Contain("Here is my reasoning.");
      response.Text.Should().NotContain("[[TOOL:");
   }

   [Fact]
   public async Task Function_Invocation_Actually_Runs_Through_The_Middleware()
   {
      // End-to-end: a provider that emits a marker once and then answers, a real tool, and the
      // standard middleware. Before the fix this made zero invocations.
      var invocations = 0;
      var provider = new ScriptedProvider(["I need a number. [[TOOL: adder {\"n\": 20}]]", "The answer is 42."]);
      var tool = AIFunctionFactory.Create(
         (int n) =>
         {
            invocations++;
            return n + 22;
         },
         new AIFunctionFactoryOptions { Name = "adder" });

      var client = provider.AsChatClient()
         .AsBuilder()
         .UseFunctionInvocation(
            loggerFactory: null,
            configure: options => options.MaximumIterationsPerRequest = 3)
         .Build();

      var response = await client.GetResponseAsync(
         [new ChatMessage(ChatRole.User, "what is 20 more than 22?")],
         new ChatOptions { Tools = [tool] });

      invocations.Should().Be(1, "the middleware must actually run the tool, not just see its name");
      response.Text.Should().Contain("The answer is 42.", "the loop resumes the model after the result");

      var toolTraffic = response.Messages
         .SelectMany(m => m.Contents)
         .OfType<FunctionCallContent>()
         .ToList();

      toolTraffic.Should().ContainSingle();
      toolTraffic[0].Name.Should().Be("adder");
   }

   [Fact]
   public async Task A_Conversation_Carries_Tool_History_Forward_Instead_Of_Dropping_It()
   {
      var recorder = new RecordingProvider();
      var client = recorder.AsChatClient();

      await client.GetResponseAsync(
      [
         new ChatMessage(ChatRole.User, "go"),
         new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "adder", new Dictionary<string, object?> { ["n"] = 2 })]),
         new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", 24)])
      ]);

      // Before the fix both messages flattened to empty strings, so the model never saw what it
      // had already asked for.
      recorder.LastUserPrompt.Should().Contain("adder");
      recorder.LastUserPrompt.Should().Contain("24");
   }

   [Fact]
   public async Task The_Iteration_Bound_Is_Honoured()
   {
      // A model that requests a tool forever must stop. The bound belongs to the middleware now,
      // not to a loop of our own.
      var provider = new ScriptedProvider(Enumerable.Repeat("[[TOOL: adder {\"n\": 1}]]", 20).ToList());
      var tool = AIFunctionFactory.Create((int n) => n, new AIFunctionFactoryOptions { Name = "adder" });

      var client = provider.AsChatClient()
         .AsBuilder()
         .UseFunctionInvocation(
            loggerFactory: null,
            configure: options => options.MaximumIterationsPerRequest = 2)
         .Build();

      var task = client.GetResponseAsync(
         [new ChatMessage(ChatRole.User, "go")],
         new ChatOptions { Tools = [tool] });

      // A model that requests a tool forever must stop. The bound belongs to the middleware now,
      // not to a loop of our own — this asserts the outcome, not an internal round-trip count.
      var act = async () => await task.WaitAsync(TimeSpan.FromSeconds(30));
      await act.Should().NotThrowAsync("the loop must terminate, not spin");
   }

   [Fact]
   public void AMarker_Split_Across_Chunks_Is_Still_Detected()
   {
      var stream = new ToolMarkerStream();

      stream.Append("text [[TOOL: adder {\"n\"", out var first).Should().Be("text ");
      first.Should().BeNull("the marker is still incomplete");

      stream.Append(": 5}]]", out var second).Should().BeEmpty();
      second.Should().NotBeNull();
      second!.ToolName.Should().Be("adder");
   }

   [Fact]
   public void Streaming_Flushes_A_Response_That_Ends_Mid_Marker()
   {
      var stream = new ToolMarkerStream();

      stream.Append("before [[TOOL: adder", out _).Should().Be("before ");
      stream.Flush().Should().Be("[[TOOL: adder", "a truncated marker is still text, not silence");
   }

   [Fact]
   public void ALiteral_Double_Bracket_That_Is_Not_A_Tool_Is_Treated_As_Prose()
   {
      var stream = new ToolMarkerStream();

      var emitted = stream.Append("see [[this is not a marker]] for details", out var marker);

      marker.Should().BeNull();
      emitted.Should().Contain("this is not a marker");
   }

   [Fact]
   public async Task The_Audit_Trail_Records_The_Result_Even_Though_It_Arrives_A_Round_Trip_Later()
   {
      // Regression: the call leaves the transport as FunctionCallContent, the tool is invoked by
      // the middleware afterwards, and the result only comes back on the NEXT request. Pairing the
      // two inside one response reported every call as failed - and the live run is what caught it.
      var invocations = 0;
      var provider = new ScriptedProvider(["Calling now. [[TOOL: adder {\"n\": 20}]]", "Got it: 42."]);
      var tool = AIFunctionFactory.Create(
         (int n) =>
         {
            invocations++;
            return n + 22;
         },
         new AIFunctionFactoryOptions { Name = "adder" });

      var client = provider.AsChatClient()
         .AsBuilder()
         .UseFunctionInvocation(
            loggerFactory: null,
            configure: options => options.MaximumIterationsPerRequest = 3)
         .Build();

      await client.GetResponseAsync(
         [new ChatMessage(ChatRole.User, "what is 20 more than 22?")],
         new ChatOptions { Tools = [tool] });

      invocations.Should().Be(1);

      // Re-run through the library so the audit trail itself is under test. A fresh provider: the
      // scripted one above has already advanced past its script.
      var member = new CouncilMember(
         "m",
         new ScriptedProvider(["Calling now. [[TOOL: adder {\"n\": 20}]]", "Got it: 42."]),
         "Analyst");
      var (_, calls) = await ToolCallingMemberExecutor.AskAsync(
         member, [tool], "You are an analyst.", "what is 20 more than 22?", 0.3f, 3, roundNumber: 1);

      calls.Should().ContainSingle();
      calls[0].Succeeded.Should().BeTrue("the tool ran, so the log must say so");
      calls[0].ErrorMessage.Should().BeNull();
      calls[0].Result.Should().Contain("42", "the value the tool actually returned");
   }

   [Fact]
   public async Task A_Call_The_Loop_Abandons_Is_Reported_As_Producing_Nothing()
   {
      // The model asks for a tool and then keeps asking, until the bound stops it. The last call
      // never gets a result, and must be visible rather than dropped.
      var provider = new ScriptedProvider(
         ["[[TOOL: adder {\"n\": 1}]]", "[[TOOL: adder {\"n\": 1}]]", "[[TOOL: adder {\"n\": 1}]]", "[[TOOL: adder {\"n\": 1}]]", "[[TOOL: adder {\"n\": 1}]]"]);
      var tool = AIFunctionFactory.Create((int n) => n, new AIFunctionFactoryOptions { Name = "adder" });

      var member = new CouncilMember("m", provider, "Analyst");
      var (_, calls) = await ToolCallingMemberExecutor.AskAsync(
         member, [tool], "You are an analyst.", "go", 0.3f, maxIterations: 3, roundNumber: 1);

      calls.Should().NotBeEmpty();
      calls.Should().AllSatisfy(c =>
         (c.Succeeded || c.ErrorMessage is not null).Should().BeTrue(
            "every recorded call must say whether it produced anything"));
   }

   [Fact]
   public void A_NonPositive_Token_Budget_Is_Rejected()
   {
      var act = () => new TokenBudgetCostGate(0);

      act.Should().Throw<ArgumentOutOfRangeException>();
   }

   [Fact]
   public async Task AToken_Budget_Enforces_Without_Any_Pricing_Registry()
   {
      var gate = new TokenBudgetCostGate(1_000);

      var under = await gate.CheckAsync(Spend(400, 400));
      var over = await gate.CheckAsync(Spend(600, 600));

      under.IsAllowed.Should().BeTrue();
      over.IsAllowed.Should().BeFalse("token counts need no price list, so the ceiling really bites");
      over.Limit.Should().BeNull("a token gate has no money figure to report");
      over.Reason.Should().Contain("Token ceiling");
   }

   [Fact]
   public void AMoney_Ceiling_Without_Prices_Fails_At_Build_Rather_Than_Silently_Doing_Nothing()
   {
      var builder = new CouncilBuilder()
         .WithUserPrompt("q")
         .AddMember("m", new FakeLLMProvider())
         .WithCostLimit(1m);

      var act = () => builder.Build();

      act.Should().Throw<InvalidOperationException>().WithMessage("*pricing registry*");
   }

   [Fact]
   public void AToken_Budget_Builds_Without_Any_Prices()
   {
      var executor = new CouncilBuilder()
         .WithUserPrompt("q")
         .WithStandardDebate()
         .AddMember("m", new FakeLLMProvider())
         .WithTokenBudget(1_000)
         .Build();

      executor.Should().NotBeNull();
   }

   [Fact]
   public void AMoney_Ceiling_Builds_Once_Prices_Are_Configured()
   {
      var executor = new CouncilBuilder()
         .WithUserPrompt("q")
         .WithStandardDebate()
         .AddMember("m", new FakeLLMProvider())
         .WithCostLimit(1m)
         .WithPricingRegistry(new ModelPricingRegistry())
         .Build();

      executor.Should().NotBeNull();
   }

   private static CostEstimate Spend(int prompt, int completion)
      => new(0m, prompt, completion, [], IsEstimate: true, WasTruncated: false);

   /// <summary>Captures the prompt the adapter actually produced.</summary>
   private sealed class RecordingProvider(string providerName = "Fake", string reply = "ok") : ILLMProvider
   {
      public string LastSystemPrompt { get; private set; } = string.Empty;
      public string LastUserPrompt { get; private set; } = string.Empty;

      public string ProviderName => providerName;

      public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

      public Task<ModelCapabilities> GetModelCapabilitiesAsync(
         string model, CancellationToken ct = default)
         => Task.FromResult(ModelCapabilities.Unknown(model));

      public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
         => Task.FromResult<IReadOnlyList<string>>(["m"]);

      public Task<string> ChatAsync(
         string model, string systemPrompt, string userPrompt, float temperature,
         CancellationToken ct = default)
      {
         LastSystemPrompt = systemPrompt;
         LastUserPrompt = userPrompt;
         return Task.FromResult(reply);
      }

      public async IAsyncEnumerable<string> ChatStreamAsync(
         string model, string systemPrompt, string userPrompt, float temperature,
         [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
      {
         await Task.Yield();
         LastSystemPrompt = systemPrompt;
         LastUserPrompt = userPrompt;
         yield return reply;
      }

      public void Dispose()
      {
      }
   }

   /// <summary>Replays a fixed script of responses, repeating the last one forever.</summary>
   private sealed class ScriptedProvider(IReadOnlyList<string> script) : ILLMProvider
   {
      private int _index;

      public string ProviderName => "Scripted";

      public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

      public Task<ModelCapabilities> GetModelCapabilitiesAsync(
         string model, CancellationToken ct = default)
         => Task.FromResult(ModelCapabilities.Unknown(model));

      public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
         => Task.FromResult<IReadOnlyList<string>>(["m"]);

      public Task<string> ChatAsync(
         string model, string systemPrompt, string userPrompt, float temperature,
         CancellationToken ct = default)
         => Task.FromResult(Next());

      public async IAsyncEnumerable<string> ChatStreamAsync(
         string model, string systemPrompt, string userPrompt, float temperature,
         [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
      {
         await Task.Yield();
         yield return Next();
      }

      private string Next()
      {
         if (_index < script.Count - 1) return script[_index++];
         return script.Count == 0 ? "done" : script[^1];
      }

      public void Dispose()
      {
      }
   }
}