using System.Net;
using System.Text;
using System.Text.Json;
using Delibera.Core.Providers.LLM;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Unit tests for <see cref="YandexGptProvider" />.
///    Uses a delegating <see cref="HttpMessageHandler" /> so the tests run
///    without touching the real Yandex Cloud network.
/// </summary>
public static class YandexGptProviderTestExtensions
{
   /// <summary>
   ///    Replaces the inner HttpMessageHandler of a YandexGptProvider without
   ///    changing its public surface. Test-only.
   /// </summary>
   public static YandexGptProvider WithHandler(this YandexGptProvider provider, HttpMessageHandler handler)
   {
      // We use private reflection to swap the handler because the public
      // constructor does not accept an HttpClient / handler. This keeps the
      // production API simple while still making the provider unit-testable.
      var field = typeof(YandexGptProvider).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      var existing = (HttpClient)field!.GetValue(provider)!;

      var newClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
      foreach (var header in existing.DefaultRequestHeaders)
      {
         if (header.Key == "Authorization")
            continue;
         newClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
      }
      newClient.DefaultRequestHeaders.Authorization = existing.DefaultRequestHeaders.Authorization;
      field.SetValue(provider, newClient);
      existing.Dispose();
      return provider;
   }
}

public sealed class YandexGptProviderTests
{
   private const string ApiKey = "test-api-key";
   private const string FolderId = "b1testfolderid";
   private const string NewEndpoint = "https://ai.api.cloud.yandex.net/v1/responses";
   private const string LegacyEndpoint = "https://llm.api.cloud.yandex.net/foundationModels/v1/completion";

   // ── New /v1/responses endpoint ───────────────────────────────────────────

   [Fact]
   public async Task ChatAsync_NewEndpoint_SendsApiKey_AndOpenAIProject()
   {
      using var handler = new CapturingHandler(NewShapeResponse("hello from yandex"));
      using var provider = new YandexGptProvider(ApiKey, FolderId, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      var answer = await provider.ChatAsync("yandexgpt-5-lite/latest", "system", "user");

      answer.Should().Be("hello from yandex");
      handler.LastAuthorization.Should().Be($"Api-Key {ApiKey}");
      handler.LastOpenAIProject.Should().Be(FolderId);
      handler.LastAuthorizationHeaderValues.Should().HaveCount(1);
   }

   [Fact]
   public async Task ChatAsync_NewEndpoint_UsesOpenAIResponsesBody()
   {
      using var handler = new CapturingHandler(NewShapeResponse("ok"));
      using var provider = new YandexGptProvider(ApiKey, FolderId, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      await provider.ChatAsync("yandexgpt-5-lite/latest", "sys", "usr");

      handler.LastRequestBody.Should().NotBeNullOrWhiteSpace();
      using var doc = JsonDocument.Parse(handler.LastRequestBody!);
      doc.RootElement.GetProperty("model").GetString().Should().Be("yandexgpt-5-lite/latest");
      doc.RootElement.GetProperty("instructions").GetString().Should().Be("sys");
      doc.RootElement.GetProperty("input").GetString().Should().Be("usr");
   }

   [Fact]
   public async Task ChatAsync_NewEndpoint_DoesNotDuplicateAuthHeadersOnRequest()
   {
      using var handler = new CapturingHandler(NewShapeResponse("ok"));
      using var provider = new YandexGptProvider(ApiKey, FolderId, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      await provider.ChatAsync("m", "s", "u");

      handler.LastAuthorizationHeaderValues.Should().HaveCount(1,
         "Authorization must not be duplicated on the new endpoint");
      handler.LastOpenAIProjectHeaderValues.Should().HaveCount(1,
         "OpenAI-Project must not be duplicated on the new endpoint");
   }

   [Fact]
   public async Task ChatAsync_NewEndpoint_ExtractsOutputText()
   {
      using var handler = new CapturingHandler(NewShapeResponse("the answer"));
      using var provider = new YandexGptProvider(ApiKey, FolderId, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      var answer = await provider.ChatAsync("m", "s", "u");

      answer.Should().Be("the answer");
   }

   // ── Legacy Foundation Models endpoint ────────────────────────────────────

   [Fact]
   public async Task ChatAsync_LegacyEndpoint_SendsBearerToken()
   {
      using var handler = new CapturingHandler(LegacyShapeResponse("legacy answer"));
      using var provider = new YandexGptProvider(ApiKey, folderId: null, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      var answer = await provider.ChatAsync("gpt://b1/xxx/yandexgpt-lite", "system", "user");

      answer.Should().Be("legacy answer");
      handler.LastAuthorization.Should().Be($"Bearer {ApiKey}");
      handler.LastOpenAIProject.Should().BeNull();
   }

   [Fact]
   public async Task ChatAsync_LegacyEndpoint_UsesCompletionBody()
   {
      using var handler = new CapturingHandler(LegacyShapeResponse("ok"));
      using var provider = new YandexGptProvider(ApiKey, folderId: null, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      await provider.ChatAsync("gpt://b1/xxx/yandexgpt-lite", "sys", "usr", temperature: 0.42f);

      handler.LastRequestBody.Should().NotBeNullOrWhiteSpace();
      using var doc = JsonDocument.Parse(handler.LastRequestBody!);
      doc.RootElement.GetProperty("modelUri").GetString().Should().Be("gpt://b1/xxx/yandexgpt-lite");
      doc.RootElement.GetProperty("completionOptions").GetProperty("stream").GetBoolean().Should().BeFalse();
      doc.RootElement.GetProperty("completionOptions").GetProperty("temperature").GetSingle().Should().Be(0.42f);

      var messages = doc.RootElement.GetProperty("messages");
      messages.GetArrayLength().Should().Be(2);
      messages[0].GetProperty("role").GetString().Should().Be("system");
      messages[0].GetProperty("text").GetString().Should().Be("sys");
      messages[1].GetProperty("role").GetString().Should().Be("user");
      messages[1].GetProperty("text").GetString().Should().Be("usr");
   }

   [Fact]
   public async Task ChatAsync_LegacyEndpoint_PicksConfiguredLegacyUrl()
   {
      using var handler = new CapturingHandler(LegacyShapeResponse("ok"));
      using var provider = new YandexGptProvider(ApiKey, folderId: null, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      await provider.ChatAsync("m", "s", "u");

      handler.LastRequestUri.Should().Be(new Uri(LegacyEndpoint));
   }

   // ── Error handling ───────────────────────────────────────────────────────

   [Fact]
   public async Task ChatAsync_Throws_WhenApiReturnsHttpError()
   {
      using var handler = new CapturingHandler("{\"error\":\"forbidden\"}", HttpStatusCode.Forbidden);
      using var provider = new YandexGptProvider(ApiKey, FolderId, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
         () => provider.ChatAsync("m", "s", "u"));
      ex.Message.Should().Contain("Forbidden");
   }

   [Fact]
   public async Task ChatAsync_Throws_WhenApiReturnsEmbeddedModelError()
   {
      // Yandex sometimes returns HTTP 200 with an error object inside the body
      // (e.g. context-window overflow). The provider must surface that error.
      using var handler = new CapturingHandler(
         """{"error":{"code":"model_call_error","message":"number of input tokens must be no more than 32768, got 56832"},"output":[]} """,
         HttpStatusCode.OK);
      using var provider = new YandexGptProvider(ApiKey, FolderId, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
         () => provider.ChatAsync("m", "s", "u"));
      ex.Message.Should().Contain("model_call_error");
      ex.Message.Should().Contain("32768");
   }

   [Fact]
   public async Task ChatAsync_Throws_WhenResponseTextIsEmpty()
   {
      using var handler = new CapturingHandler(NewShapeResponse("{}"));
      using var provider = new YandexGptProvider(ApiKey, FolderId, NewEndpoint, LegacyEndpoint).WithHandler(handler);

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
         () => provider.ChatAsync("m", "s", "u"));
      ex.Message.Should().Contain("Empty response");
   }

   [Fact]
   public void ProviderName_IsYandexGpt()
   {
      using var provider = new YandexGptProvider(ApiKey, FolderId);
      provider.ProviderName.Should().Be("YandexGPT");
   }

   // ── helpers ─────────────────────────────────────────────────────────────

   private static string NewShapeResponse(string text) =>
      $$"""
      {
        "id": "resp-test",
        "object": "response",
        "output": [
          {
            "type": "message",
            "role": "assistant",
            "content": [
              { "type": "output_text", "text": "{{JsonEncoded(text)}}", "annotations": [] }
            ]
          }
        ],
        "usage": { "input_tokens": 1, "output_tokens": 1, "total_tokens": 2 }
      }
      """;

   private static string LegacyShapeResponse(string text) =>
      $$"""
      {
        "result": {
          "alternatives": [
            {
              "message": { "role": "assistant", "text": "{{JsonEncoded(text)}}" },
              "status": "ALTERNATIVE_STATUS_FINAL"
            }
          ],
          "usage": { "inputTextTokens": 1, "completionTokens": 1, "totalTokens": 2 },
          "modelVersion": "test"
        }
      }
      """;

   private static string JsonEncoded(string value)
   {
      // Minimal JSON string escaping for the canned payloads above.
      return value
         .Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");
   }

   /// <summary>
   ///    Captures request and returns a canned response.
   /// </summary>
   private sealed class CapturingHandler : HttpMessageHandler
   {
      private readonly string _responseBody;
      private readonly HttpStatusCode _statusCode;

      public CapturingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
      {
         _responseBody = responseBody;
         _statusCode = statusCode;
      }

      public Uri? LastRequestUri { get; private set; }
      public string? LastAuthorization { get; private set; }
      public string? LastOpenAIProject { get; private set; }
      public IEnumerable<string>? LastAuthorizationHeaderValues { get; private set; }
      public IEnumerable<string>? LastOpenAIProjectHeaderValues { get; private set; }
      public string? LastRequestBody { get; private set; }

      protected override async Task<HttpResponseMessage> SendAsync(
         HttpRequestMessage request, CancellationToken cancellationToken)
      {
         LastRequestUri = request.RequestUri;
         LastAuthorization = request.Headers.Authorization?.ToString();
         LastAuthorizationHeaderValues = request.Headers.TryGetValues("Authorization", out var authValues)
            ? authValues.ToList()
            : [];
         LastOpenAIProjectHeaderValues = request.Headers.TryGetValues("OpenAI-Project", out var projValues)
            ? projValues.ToList()
            : [];
         LastOpenAIProject = LastOpenAIProjectHeaderValues.Any()
            ? string.Join(",", LastOpenAIProjectHeaderValues)
            : null;
         if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

         return new HttpResponseMessage(_statusCode)
         {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
         };
      }
   }
}
