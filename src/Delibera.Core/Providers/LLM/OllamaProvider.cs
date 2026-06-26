using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using ChatRole = OllamaSharp.Models.Chat.ChatRole;

namespace Delibera.Core.Providers.LLM;

/// <summary>
///    Connection mode for an <see cref="OllamaProvider" />.
/// </summary>
public enum OllamaConnectionMode
{
   /// <summary>Talking to a local Ollama server (no API key, no Cloudflare, no 524).</summary>
   Local,

   /// <summary>Talking to Ollama Cloud (API key required, behind Cloudflare, may return 524/429).</summary>
   Cloud
}

/// <summary>
///    Ollama LLM provider backed by OllamaSharp.
///    Works with both a local Ollama server and Ollama Cloud.
/// </summary>
/// <remarks>
///    Use <see cref="ForLocal" /> for a local server (e.g. <c>http://localhost:11434</c>) and
///    <see cref="ForCloud" /> for Ollama Cloud (e.g. <c>https://api.ollama.com</c>) with an API key.
///    The provider applies mode-appropriate retry/backoff: cloud calls retry transient
///    524/429/5xx responses (Cloudflare origin timeouts) and local calls retry connection
///    failures. Both honour the user's <see cref="CancellationToken" />.
/// </remarks>
public sealed class OllamaProvider : ILLMProvider
{
   private const int DefaultMaxAttempts = 3;
   private static readonly TimeSpan DefaultCloudTimeout = TimeSpan.FromMinutes(5);
   private static readonly TimeSpan DefaultLocalTimeout = TimeSpan.FromMinutes(10);

   private bool _disposed;

   /// <summary>
   ///    Creates an Ollama provider. The mode is inferred from <paramref name="apiKey" />:
   ///    a non-empty key selects <see cref="OllamaConnectionMode.Cloud" />, otherwise
   ///    <see cref="OllamaConnectionMode.Local" />. Prefer <see cref="ForLocal" /> /
   ///    <see cref="ForCloud" /> for explicit control.
   /// </summary>
   /// <param name="endpoint">Ollama endpoint URL (e.g. "https://api.ollama.com" or "http://localhost:11434").</param>
   /// <param name="apiKey">API key for Ollama Cloud (empty for local server).</param>
   /// <param name="timeout">Optional HTTP timeout. Defaults to 5 min for cloud, 10 min for local.</param>
   public OllamaProvider(string endpoint, string apiKey = "", TimeSpan? timeout = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

      var uri = new Uri(endpoint.TrimEnd('/'));
      Mode = string.IsNullOrWhiteSpace(apiKey)
         ? OllamaConnectionMode.Local
         : OllamaConnectionMode.Cloud;

      var effectiveTimeout = timeout ??
                             (Mode == OllamaConnectionMode.Cloud
                                ? DefaultCloudTimeout
                                : DefaultLocalTimeout);

      var httpClient = new HttpClient { BaseAddress = uri, Timeout = effectiveTimeout };
       if (Mode == OllamaConnectionMode.Cloud)
          httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey.Trim()}");

      Client = new OllamaApiClient(httpClient);
   }

   private OllamaProvider(Uri uri, string apiKey, OllamaConnectionMode mode, TimeSpan timeout)
   {
      Mode = mode;
      var httpClient = new HttpClient { BaseAddress = uri, Timeout = timeout };
      if (mode == OllamaConnectionMode.Cloud)
         httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey.Trim()}");

      Client = new OllamaApiClient(httpClient);
   }

   /// <summary>The connection mode this provider was configured with.</summary>
   public OllamaConnectionMode Mode { get; }

   /// <summary>Provides access to the underlying OllamaSharp client (used by <see cref="OllamaEmbeddingProvider" />).</summary>
   internal OllamaApiClient Client { get; }

   /// <inheritdoc />
   public string ProviderName => Mode == OllamaConnectionMode.Cloud
      ? "OllamaCloud"
      : "Ollama";

   /// <inheritdoc />
   public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
   {
      try
      {
         await Client.ListLocalModelsAsync(ct);
         return true;
      }
      catch
      {
         return false;
      }
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
   {
      try
      {
         var models = await Client.ListLocalModelsAsync(ct);
         return models.Select(m => m.Name).ToList().AsReadOnly();
      }
      catch (Exception ex)
      {
         throw new InvalidOperationException($"Failed to list Ollama models: {ex.Message}", ex);
      }
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

      var messages = new List<Message>();
      if (!string.IsNullOrWhiteSpace(systemPrompt))
         messages.Add(new Message(ChatRole.System, systemPrompt));
      messages.Add(new Message(ChatRole.User, userPrompt));

      var request = new ChatRequest
      {
         Model = model,
         Messages = messages,
         Options = new RequestOptions { Temperature = temperature }
      };

      var maxAttempts = Mode == OllamaConnectionMode.Cloud
         ? DefaultMaxAttempts
         : 2;
      var delay = TimeSpan.FromSeconds(2);

      for (var attempt = 1;; attempt++)
      {
         var sb = new StringBuilder();
         try
         {
            await foreach (var chunk in Client.ChatAsync(request, ct))
            {
               if (chunk is not { Message.Content: { } content })
                  continue;
               sb.Append(content);
            }

            var response = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(response))
               throw new InvalidOperationException($"Empty response from model '{model}'.");
            return response;
         }
         catch (HttpRequestException ex)
         {
            if (ct.IsCancellationRequested) throw;
            if (attempt < maxAttempts && IsTransientHttp(ex, Mode))
            {
               await Task.Delay(delay, ct);
               delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
               continue;
            }

            throw new InvalidOperationException($"HTTP error talking to Ollama (model: {model}): {ex.Message}", ex);
         }
         catch (TaskCanceledException) when (ct.IsCancellationRequested)
         {
            throw;
         }
         catch (TaskCanceledException ex)
         {
            if (attempt < maxAttempts)
            {
               await Task.Delay(delay, ct);
               delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
               continue;
            }

            throw new TimeoutException($"Request to Ollama model '{model}' timed out.", ex);
         }
      }
   }

   /// <inheritdoc />
    public void Dispose()
    {
       if (_disposed) return;
       _disposed = true;
    }

   /// <summary>Creates a provider for a local Ollama server (e.g. <c>http://localhost:11434</c>).</summary>
   public static OllamaProvider ForLocal(string endpoint, TimeSpan? timeout = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      return new OllamaProvider(new Uri(endpoint.TrimEnd('/')), "", OllamaConnectionMode.Local,
         timeout ?? DefaultLocalTimeout);
   }

   /// <summary>Creates a provider for Ollama Cloud (e.g. <c>https://api.ollama.com</c>).</summary>
   public static OllamaProvider ForCloud(string endpoint, string apiKey, TimeSpan? timeout = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
      return new OllamaProvider(new Uri(endpoint.TrimEnd('/')), apiKey, OllamaConnectionMode.Cloud,
         timeout ?? DefaultCloudTimeout);
   }

   /// <summary>
   ///    Determines whether an <see cref="HttpRequestException" /> represents a transient failure
   ///    worth retrying. For <see cref="OllamaConnectionMode.Cloud" /> this includes 524 origin
   ///    timeouts, 429 rate-limits and any 5xx. For <see cref="OllamaConnectionMode.Local" /> only
   ///    connection-level failures (no status code) are retried — a local Ollama rarely returns
   ///    5xx, and when it does it's usually a model-load error that won't fix itself.
   /// </summary>
   private static bool IsTransientHttp(HttpRequestException ex, OllamaConnectionMode mode)
   {
      var code = ex.StatusCode;
      if (code is null) return true;
      if (mode == OllamaConnectionMode.Local) return false;

      var i = (int)code;
      return i == 429 || i == 524 || (i >= 500 && i < 600);
   }

   /// <summary>
   ///    Exposes the underlying OllamaSharp client as a Microsoft.Extensions.AI
   ///    <see cref="Microsoft.Extensions.AI.IChatClient" />.
   /// </summary>
   /// <remarks>
   ///    <see cref="OllamaApiClient" /> natively implements <see cref="Microsoft.Extensions.AI.IChatClient" />, so this lets
   ///    the Ollama provider plug into the standard Microsoft.Extensions.AI middleware pipeline
   ///    (function invocation, caching, telemetry).
   /// </remarks>
   public IChatClient AsChatClient()
   {
      return Client;
   }

   /// <summary>
   ///    Exposes the underlying OllamaSharp client as a Microsoft.Extensions.AI embedding generator.
   /// </summary>
   public IEmbeddingGenerator<string, Embedding<float>> AsEmbeddingGenerator()
   {
      return Client;
   }
}