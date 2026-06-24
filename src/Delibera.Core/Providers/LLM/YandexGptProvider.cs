using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delibera.Core.Providers.LLM;

/// <summary>
///    YandexGPT / Yandex Cloud AI Studio provider for Delibera councils.
///    Implements <see cref="ILLMProvider" /> so any Yandex model can be used
///    as a council member or chairman alongside Ollama / OpenAI models.
/// </summary>
/// <remarks>
///    Supports both the OpenAI Responses-compatible gateway
///    (<c>https://ai.api.cloud.yandex.net/v1/responses</c>) and the legacy
///    Foundation Models endpoint. The gateway is selected automatically
///    when <c>FolderId</c> is non-empty.
/// </remarks>
public sealed class YandexGptProvider : ILLMProvider
{
   private static readonly JsonSerializerOptions JsonOptions = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      WriteIndented = false
   };

   private readonly string _apiKey;
   private readonly string _endpoint;
   private readonly string _folderId;
   private readonly HttpClient _http;
   private readonly string _legacyEndpoint;
   private readonly int _maxOutputTokens;
   private readonly float _temperature;
   private bool _disposed;

   /// <summary>
   ///    Creates a YandexGPT provider.
   /// </summary>
   /// <param name="apiKey">Yandex Cloud API key.</param>
   /// <param name="folderId">Yandex Cloud folder id (empty for legacy Bearer endpoint).</param>
   /// <param name="endpoint">OpenAI-compatible responses endpoint.</param>
   /// <param name="legacyEndpoint">Legacy Foundation Models completion endpoint.</param>
   /// <param name="temperature">Default sampling temperature.</param>
   /// <param name="maxOutputTokens">Maximum output tokens per request.</param>
   public YandexGptProvider(
      string apiKey,
      string? folderId = null,
      string endpoint = "https://ai.api.cloud.yandex.net/v1/responses",
      string legacyEndpoint = "https://llm.api.cloud.yandex.net/foundationModels/v1/completion",
      float temperature = 0.3f,
      int maxOutputTokens = 4000)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

      _apiKey = apiKey;
      _folderId = folderId ?? string.Empty;
      _endpoint = endpoint.TrimEnd('/');
      _legacyEndpoint = legacyEndpoint.TrimEnd('/');
      _temperature = temperature;
      _maxOutputTokens = maxOutputTokens;

      _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

      // Use the new OpenAI-compatible gateway when a folder id is configured.
      // The legacy Bearer endpoint is kept for backward compatibility only.
      if (!string.IsNullOrWhiteSpace(_folderId))
      {
         _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Api-Key", _apiKey);
         _http.DefaultRequestHeaders.Add("OpenAI-Project", _folderId);
      }
      else
      {
         _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
      }
   }

   /// <summary>Whether the provider uses the new /v1/responses gateway.</summary>
   private bool UseNewEndpoint => !string.IsNullOrWhiteSpace(_folderId);

   /// <inheritdoc />
   public string ProviderName => "YandexGPT";

   /// <inheritdoc />
   public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
   {
      try
      {
         // Yandex does not publish a stable /models URL on either gateway,
         // so we treat the configured endpoint itself as the liveness probe.
         using var request = new HttpRequestMessage(HttpMethod.Get, UseNewEndpoint
            ? _endpoint
            : _legacyEndpoint);
         using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
         return response.IsSuccessStatusCode;
      }
      catch
      {
         return false;
      }
   }

   /// <inheritdoc />
   public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
   {
      // Yandex does not expose a standard model enumeration on either endpoint,
      // so we return the most commonly used model URIs as placeholders.
      IReadOnlyList<string> models =
      [
         "yandexgpt-5-lite/latest",
         "yandexgpt-5-pro/latest",
         "yandexgpt-lite/latest",
         "yandexgpt/latest",
         "yandexgpt-32k/latest"
      ];
      return Task.FromResult(models);
   }

   /// <inheritdoc />
   public async Task<string> ChatAsync(
      string model,
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(model);
      ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

      object requestBody;
      string endpoint;
      HttpRequestHeaders headers;

      if (UseNewEndpoint)
      {
         requestBody = new
         {
            model,
            instructions = systemPrompt,
            input = userPrompt
         };
         endpoint = _endpoint;
         headers = BuildNewAuthHeaders();
      }
      else
      {
         requestBody = new YandexCompletionRequest(
            model,
            new CompletionOptions(false, temperature, _maxOutputTokens),
            [
               new Message("system", systemPrompt),
               new Message("user", userPrompt)
            ]);
         endpoint = _legacyEndpoint;
         headers = BuildLegacyAuthHeaders();
      }

      var json = JsonSerializer.Serialize(requestBody, JsonOptions);
      using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
      request.Content = new StringContent(json, Encoding.UTF8, "application/json");

      // On the legacy endpoint auth headers travel with each request.
      // On the new endpoint they were already configured on the HttpClient
      // in the constructor (Authorization: Api-Key … and OpenAI-Project).
      if (!UseNewEndpoint)
         foreach (var h in headers)
            request.Headers.TryAddWithoutValidation(h.Key, h.Value);

      using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
      var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

      if (!response.IsSuccessStatusCode)
         throw new InvalidOperationException($"YandexGPT API failed: {response.StatusCode} — {content}");

      using var doc = JsonDocument.Parse(content);

      // Yandex returns HTTP 200 with an embedded error object when the request
      // is syntactically valid but the model call fails (context overflow,
      // content policy, rate limit, etc.). Surface that error instead of
      // swallowing it as an "empty response".
      if (doc.RootElement.TryGetProperty("error", out var errorElement) &&
          errorElement.ValueKind == JsonValueKind.Object)
      {
         var errorCode = errorElement.TryGetProperty("code", out var c)
            ? c.GetString()
            : null;
         var errorMessage = errorElement.TryGetProperty("message", out var m)
            ? m.GetString()
            : null;
         throw new InvalidOperationException(
            $"YandexGPT model call failed ({errorCode ?? "unknown"}): {errorMessage ?? content}");
      }

      var text = UseNewEndpoint
         ? ExtractNewShape(doc.RootElement)
         : ExtractLegacyShape(doc.RootElement);

      if (string.IsNullOrWhiteSpace(text) || text == "{}")
         throw new InvalidOperationException($"Empty response from YandexGPT model '{model}'. Raw body: {content}");

      return text;
   }

   /// <inheritdoc />
   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      _http.Dispose();
      GC.SuppressFinalize(this);
   }

   private void AddAuthHeaders(HttpRequestHeaders headers)
   {
      if (UseNewEndpoint)
      {
         headers.TryAddWithoutValidation("Authorization", $"Api-Key {_apiKey}");
         headers.TryAddWithoutValidation("OpenAI-Project", _folderId);
      }
      else
      {
         headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
      }
   }

   private HttpRequestHeaders BuildNewAuthHeaders()
   {
      var headers = new HttpRequestMessage().Headers;
      AddAuthHeaders(headers);
      return headers;
   }

   private HttpRequestHeaders BuildLegacyAuthHeaders()
   {
      var headers = new HttpRequestMessage().Headers;
      AddAuthHeaders(headers);
      return headers;
   }

   private static string ExtractNewShape(JsonElement root)
   {
      if (!root.TryGetProperty("output", out var output) || output.GetArrayLength() == 0)
         return "{}";
      var first = output[0];
      if (!first.TryGetProperty("content", out var content) || content.GetArrayLength() == 0)
         return "{}";
      var firstContent = content[0];
      if (!firstContent.TryGetProperty("text", out var text))
         return "{}";
      return text.GetString() ?? "{}";
   }

   private static string ExtractLegacyShape(JsonElement root)
   {
      return root
                .GetProperty("result")
                .GetProperty("alternatives")[0]
                .GetProperty("message")
                .GetProperty("text")
                .GetString() ??
             "{}";
   }

   private record YandexCompletionRequest(
      [property: JsonPropertyName("modelUri")]
      string ModelUri,
      [property: JsonPropertyName("completionOptions")]
      CompletionOptions CompletionOptions,
      [property: JsonPropertyName("messages")]
      Message[] Messages
   );

   private record CompletionOptions(
      [property: JsonPropertyName("stream")] bool Stream,
      [property: JsonPropertyName("temperature")]
      float Temperature,
      [property: JsonPropertyName("maxTokens")]
      int MaxTokens
   );

   private record Message(
      [property: JsonPropertyName("role")] string Role,
      [property: JsonPropertyName("text")] string Text
   );
}