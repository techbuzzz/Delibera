using System.Net;
using System.Text;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Regression tests for the two defects found by the 2026-10-06 Ollama Cloud performance run.
///    <para>
///    1. A participant that threw used to be handed to the Chairman as the literal string
///       <c>"[ERROR: ...]"</c>, so a verdict built from a partial council looked complete. With a
///       reasoning model this fired constantly: the model spent its whole generation budget
///       thinking, was cut off at the limit, and produced no answer at all.
///    2. <see cref="OllamaProvider" /> defaulted to <c>NumPredict = -1</c>, which Ollama Cloud
///       rejects outright — <c>"max_tokens must be positive, got: -1"</c> — so every call against
///       Ollama Cloud failed before a token was generated.
///    </para>
/// </summary>
public sealed class EmptyResponseHandlingTests
{
   private const string FailingModel = "flaky";

   // ──────────────────────────────────────────────
   // Transcript integrity
   // ──────────────────────────────────────────────

   /// <summary>Provider that answers normally for every model except <paramref name="failingModel" />.</summary>
   private sealed class SelectiveFailureProvider(string failingModel) : ILLMProvider
   {
      public string ProviderName => "SelectiveFailure";

      public Task<string> ChatAsync(
         string model, string systemPrompt, string userPrompt,
         float temperature = 0.7f, CancellationToken ct = default)
      {
         if (string.Equals(model, failingModel, StringComparison.Ordinal))
            throw new OllamaEmptyResponseException(model, "length", reasoningChars: 5249);

         return Task.FromResult($"answer from {model}");
      }

      public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

      public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
         Task.FromResult<IReadOnlyList<string>>(["good", failingModel, "third"]);

      public Task<ModelCapabilities> GetModelCapabilitiesAsync(string model, CancellationToken ct = default) =>
         Task.FromResult(ModelCapabilities.Unknown(model));

      public void Dispose()
      {
         // Nothing to release.
      }
   }

   private static async Task<DebateResult> RunAsync(ILLMProvider provider)
   {
      var builder = new CouncilBuilder()
         .WithUserPrompt("What problem does an API gateway solve?")
         .WithMaxRounds(1)
         .WithStandardDebate()
         .AddMember("good", provider)
         .AddMember(FailingModel, provider, "Expert")
         .AddMember("third", provider);

      return await builder.Build().ExecuteAsync().ConfigureAwait(false);
   }

   [Fact]
   public async Task Failed_Member_Is_Not_Presented_To_The_Chairman_As_An_Opinion()
   {
      var result = await RunAsync(new SelectiveFailureProvider(FailingModel));

      result.Rounds.Should().NotBeEmpty();
      foreach (var round in result.Rounds)
         foreach (var (_, response) in round.Responses)
            response.Should().NotContain("[ERROR:",
               "an error string in the transcript is indistinguishable from a real opinion");
   }

   [Fact]
   public async Task Surviving_Members_Still_Contribute_When_One_Fails()
   {
      var result = await RunAsync(new SelectiveFailureProvider(FailingModel));

      result.Rounds
         .SelectMany(r => r.Responses.Values)
         .Should()
         .NotBeEmpty("the round must still carry the opinions of the members that worked");

      result.Rounds
         .Where(r => r.Responses.Count < 3)
         .Should()
         .NotBeEmpty("the round containing the failing member shows fewer responses than members");
   }

   [Fact]
   public async Task Failure_Is_Recorded_With_Enough_Context_To_Diagnose_It()
   {
      var result = await RunAsync(new SelectiveFailureProvider(FailingModel));

      result.IsDegraded.Should().BeTrue();
      result.FailedMembers.Should().NotBeEmpty();

      var failure = result.FailedMembers[0];
      failure.Model.Should().Be(FailingModel);
      failure.Error.Should().NotBeNullOrWhiteSpace();
      failure.RoundNumber.Should().BeGreaterThan(0);
      failure.RoundName.Should().NotBeNullOrWhiteSpace();
      failure.ToString().Should().Contain(FailingModel);
   }

   [Fact]
   public async Task Debate_With_No_Failures_Is_Not_Marked_Degraded()
   {
      var result = await RunAsync(new SelectiveFailureProvider("never-fails"));

      result.IsDegraded.Should().BeFalse();
      result.FailedMembers.Should().BeEmpty();
   }

   // ──────────────────────────────────────────────
   // Empty-response diagnostics
   // ──────────────────────────────────────────────

   [Theory]
   [InlineData("length", 5249, true)]
   [InlineData("stop", 0, false)]
   public void Empty_Response_Exception_Separates_Budget_Exhaustion_From_A_Silent_Model(
      string doneReason, int reasoningChars, bool expectBudgetFlag)
   {
      var ex = new OllamaEmptyResponseException("glm-5.3-flash", doneReason, reasoningChars);

      ex.BudgetConsumedByReasoning.Should().Be(expectBudgetFlag);
      ex.Model.Should().Be("glm-5.3-flash");
      ex.DoneReason.Should().Be(doneReason);
   }

   [Fact]
   public void Empty_Response_Exception_Explains_How_To_Fix_Budget_Exhaustion()
   {
      var ex = new OllamaEmptyResponseException("glm-5.3-flash", "length", 5249);

      ex.Message.Should().Contain("reasoning");
      ex.Message.Should().Contain("enableThinking",
         "the actionable remedy for a reasoning model is to stop paying for reasoning");
   }

   // ──────────────────────────────────────────────
   // num_predict must never reach Ollama Cloud as a negative value
   // ──────────────────────────────────────────────

   /// <summary>Captures the JSON body the provider sends to /api/chat.</summary>
   private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
   {
      public string Body { get; private set; } = string.Empty;

      protected override async Task<HttpResponseMessage> SendAsync(
         HttpRequestMessage request, CancellationToken cancellationToken)
      {
         if (request.Content is not null)
            Body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

         return new HttpResponseMessage(HttpStatusCode.OK)
         {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/x-ndjson")
         };
      }
   }

   private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
   {
      public HttpClient CreateClient(string name) => client;
   }

   private static string StreamBody(string content) =>
      "{\"model\":\"m\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"" +
      content + "\"},\"done\":true,\"done_reason\":\"stop\",\"total_duration\":1,\"eval_count\":1}\n";

   private static async Task<string> CaptureRequestBodyAsync(int maxOutputTokens)
   {
      var handler = new CapturingHandler(StreamBody("hello"));
      var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.ollama.com") };

      var provider = new OllamaProvider(
         "https://api.ollama.com",
         "test-key",
         timeout: null,
         httpClientFactory: new SingleClientFactory(client),
         resilienceProvider: null,
         maxOutputTokens: maxOutputTokens);

      await provider.ChatAsync("m", "sys", "user").ConfigureAwait(false);
      return handler.Body;
   }

   [Fact]
   public async Task Default_Configuration_Omits_Output_Cap_Instead_Of_Sending_Negative_NumPredict()
   {
      // The literal failure seen against api.ollama.com was:
      //   OllamaException: max_tokens must be positive, got: -1
      var body = await CaptureRequestBodyAsync(maxOutputTokens: -1);

      body.Should().NotContain("\"num_predict\":-1");
      body.Should().NotContain("-1,\"", "a negative num_predict anywhere in the options block");
   }

   [Fact]
   public async Task Positive_Output_Cap_Is_Sent_Through()
   {
      var body = await CaptureRequestBodyAsync(maxOutputTokens: 4096);

      body.Should().Contain("\"num_predict\":4096");
   }

   [Fact]
   public async Task Reasoning_Is_Disabled_By_So_Reasoning_Models_Do_Not_Burn_The_Answer_Budget()
   {
      var body = await CaptureRequestBodyAsync(maxOutputTokens: -1);

      body.Should().Contain("\"think\":false");
   }
}