using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delibera.Core.Resilience;

namespace Delibera.Core.Providers.LLM;

/// <summary>
///    YandexGPT / Yandex Cloud AI Studio provider for Delibera councils.
///    Implements <see cref="ILLMProvider" /> so any Yandex model can be used
///    as a council member or chairman alongside Ollama / OpenAI models.
/// </summary>
/// <remarks>
///    <para>
///       Supports both the OpenAI Responses-compatible gateway
///       (<c>https://ai.api.cloud.yandex.net/v1/responses</c>) and the legacy
///       Foundation Models endpoint. The gateway is selected automatically
///       when <c>FolderId</c> is non-empty.
///    </para>
///    <para>
///       Transient failures (connection drops, 429/5xx, Cloudflare 524) are
///       retried by a Polly v8 resilience pipeline obtained from the
///       <see cref="IDeliberaResiliencePipelineProvider" /> registered in DI
///       (default pipeline: <c>Delibera.Cloud</c>).
///       When the provider is constructed without DI the pipeline is a no-op
///       — the same behavior as the previous manual-error path.
///    </para>
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
   private readonly Polly.ResiliencePipeline<HttpResponseMessage>? _pipeline;
   private readonly IHttpClientFactory? _httpClientFactory;
   private readonly string? _httpClientName;
   private readonly string _legacyEndpoint;
   private readonly int _maxOutputTokens;
   private bool _disposed;

   /// <summary>
   ///    Creates a YandexGPT provider with no resilience pipeline. Errors
   ///    propagate directly to the caller — backward-compatible with v10.2.
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
      : this(apiKey, folderId, endpoint, legacyEndpoint, temperature, maxOutputTokens,
         httpClientFactory: null, resilienceProvider: null, httpClientName: null, pipelineName: null)
   {
   }

   /// <summary>
   ///    Creates a YandexGPT provider wired to an <see cref="IHttpClientFactory" /> and a Polly v8
   ///    <see cref="Polly.ResiliencePipeline{TResult}" />. Transient failures are retried automatically.
   /// </summary>
   /// <param name="apiKey">Yandex Cloud API key.</param>
   /// <param name="folderId">Yandex Cloud folder id (empty for legacy Bearer endpoint).</param>
   /// <param name="endpoint">OpenAI-compatible responses endpoint.</param>
   /// <param name="legacyEndpoint">Legacy Foundation Models completion endpoint.</param>
   /// <param name="temperature">Default sampling temperature.</param>
   /// <param name="maxOutputTokens">Maximum output tokens per request.</param>
   /// <param name="httpClientFactory">
   ///    Optional factory that produces the configured <see cref="HttpClient" />.
   ///    When <c>null</c> the provider creates its own HttpClient (no factory reuse, no resilience).
   /// </param>
   /// <param name="resilienceProvider">
   ///    Optional pipeline registry. When <c>null</c> the provider falls back to a no-op pipeline
   ///    (errors propagate without retries).
   /// </param>
   /// <param name="httpClientName">
   ///    Logical name used with <paramref name="httpClientFactory" />. When <c>null</c> defaults to
   ///    <c>"Delibera.YandexGPT"</c>.
   /// </param>
   /// <param name="pipelineName">
   ///    Pipeline key looked up from <paramref name="resilienceProvider" />.
   ///    When <c>null</c> defaults to <c>Delibera.Cloud</c>.
   /// </param>
   public YandexGptProvider(
      string apiKey,
      string? folderId,
      string endpoint,
      string legacyEndpoint,
      float temperature,
      int maxOutputTokens,
      IHttpClientFactory? httpClientFactory,
      IDeliberaResiliencePipelineProvider? resilienceProvider,
      string? httpClientName = null,
      string? pipelineName = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

      _apiKey = apiKey;
      _folderId = folderId ?? string.Empty;
      _endpoint = endpoint.TrimEnd('/');
      _legacyEndpoint = legacyEndpoint.TrimEnd('/');
      _maxOutputTokens = maxOutputTokens;
      _ = temperature; // accepted for API compatibility; per-call temperature is passed to ChatAsync

      var name = string.IsNullOrWhiteSpace(httpClientName) ? "Delibera.YandexGPT" : httpClientName;
      var pipeline = resilienceProvider?.GetPipeline(pipelineName) ?? Polly.ResiliencePipeline<HttpResponseMessage>.Empty;

      if (httpClientFactory is not null)
      {
         _httpClientFactory = httpClientFactory;
         _httpClientName = name;
         _pipeline = pipeline;
         // Lazily resolved through HttpClientFactory inside ChatAsync so the
         // factory owns the handler/timeout/lifetime. We still hold a local
         // "anchor" HttpClient for IsAvailableAsync (liveness probe) and as
         // fallback when no factory is registered.
      }
      else
      {
         _httpClientFactory = null;
         _httpClientName = null;
         _pipeline = null;
      }

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
         using var response = await SendAsync(request, ct).ConfigureAwait(false);
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
   /// <remarks>
   ///    YandexGPT does not expose a capabilities endpoint, so this falls back to the
   ///    static <see cref="ModelContextWindowRegistry" />. Known Yandex models are
   ///    pre-registered there (yandexgpt-5 = 32K, yandexgpt-32k = 32K, yandexgpt = 8K).
   /// </remarks>
   public Task<ModelCapabilities?> GetModelCapabilitiesAsync(string model, CancellationToken ct = default)
   {
      var window = ModelContextWindowRegistry.GetContextWindow(model);
      if (window is not null)
      {
         return Task.FromResult<ModelCapabilities?>(
            new ModelCapabilities { ModelName = model, ContextWindowTokens = window });
      }

      return Task.FromResult<ModelCapabilities?>(null);
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
         requestBody = new Dictionary<string, object?>
         {
            ["modelUri"] = model,
            ["completionOptions"] = new Dictionary<string, object?>
            {
               ["stream"] = false,
               ["temperature"] = temperature,
               ["maxTokens"] = _maxOutputTokens
            },
            ["messages"] = new[]
            {
               new Dictionary<string, object?> { ["role"] = "system", ["text"] = systemPrompt },
               new Dictionary<string, object?> { ["role"] = "user", ["text"] = userPrompt }
            }
         };
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

      using var response = await SendAsync(request, ct).ConfigureAwait(false);
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
      if (_httpClientFactory is null)
         _http.Dispose();
   }

   /// <summary>
   ///    Issues the request through either the DI-managed HttpClient (when configured) or the
   ///    locally-owned <see cref="HttpClient" />. Retries are applied only when the DI path is active.
   /// </summary>
   private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
   {
      if (_httpClientFactory is not null && _pipeline is not null)
      {
         var client = _httpClientFactory.CreateClient(_httpClientName!);
         return await client.SendAsync(_pipeline, request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
      }

      return await _http.SendAsync(request, ct).ConfigureAwait(false);
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

}
