using Delibera.Core.DependencyInjection;
using Delibera.Core.Resilience;
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
///    <para>
///    Use <c>ForLocal</c> for a local server (e.g. <c>http://localhost:11434</c>) and
///    <c>ForCloud</c> for Ollama Cloud (e.g. <c>https://api.ollama.com</c>) with an API key.
///    </para>
///    <para>
///       Transient failures are retried by a Polly v8
///       <see cref="Polly.ResiliencePipeline{TResult}" /> obtained from the
///       <see cref="IDeliberaResiliencePipelineProvider" /> registered in DI.
///       Local mode uses the
///       <see cref="ResilienceOptions.LocalPipelineName" /> pipeline
///       (connection-level retries only); cloud mode uses
///       <see cref="ResilienceOptions.CloudPipelineName" />
///       (HTTP 408/429/500/502/503/504/524 retries + connection failures).
///       When constructed without DI the pipeline is a no-op — the legacy behaviour
///       before v10.2.2 was no retry at all, so the public surface is fully
///       backward-compatible.
///    </para>
/// </remarks>
public sealed class OllamaProvider : ILLMProvider
{
   private static readonly TimeSpan DefaultCloudTimeout = TimeSpan.FromMinutes(5);
   private static readonly TimeSpan DefaultLocalTimeout = TimeSpan.FromMinutes(10);

   private readonly IHttpClientFactory? _httpClientFactory;
   private readonly string? _httpClientName;
   private readonly Polly.ResiliencePipeline? _pipeline;
   private bool _disposed;

   /// <summary>Creates an Ollama provider. The mode is inferred from <paramref name="apiKey" />: non-empty selects cloud.</summary>
   public OllamaProvider(string endpoint, string apiKey = "", TimeSpan? timeout = null)
      : this(endpoint, apiKey, timeout, httpClientFactory: null, resilienceProvider: null,
         httpClientName: null, pipelineName: null,
         mode: string.IsNullOrWhiteSpace(apiKey) ? OllamaConnectionMode.Local : OllamaConnectionMode.Cloud)
   {
   }

   /// <summary>
   ///    Creates an Ollama provider wired to an <see cref="IHttpClientFactory" /> and a Polly v8
   ///    resilience pipeline. When <paramref name="httpClientFactory" /> is <c>null</c> the
   ///    provider creates its own <see cref="HttpClient" /> (no factory reuse, no resilience) —
   ///    this preserves the v10.2.x behaviour for callers that don't use DI.
   /// </summary>
   /// <param name="endpoint">Ollama endpoint URL.</param>
   /// <param name="apiKey">API key (empty for local server).</param>
   /// <param name="timeout">HTTP timeout (null = default per mode).</param>
   /// <param name="httpClientFactory">Optional factory for handler pooling and socket reuse.</param>
   /// <param name="resilienceProvider">Optional pipeline registry (null = no retries).</param>
   /// <param name="httpClientName">Logical HttpClient name; defaults to <c>Delibera.Ollama.{Mode}</c>.</param>
   /// <param name="pipelineName">Pipeline key; defaults to Local or Cloud pipeline by mode.</param>
   /// <param name="mode">Explicit connection mode override (legacy <c>ForLocal</c>/<c>ForCloud</c> only).</param>
   public OllamaProvider(
      string endpoint,
      string apiKey,
      TimeSpan? timeout,
      IHttpClientFactory? httpClientFactory,
      IDeliberaResiliencePipelineProvider? resilienceProvider,
      string? httpClientName = null,
      string? pipelineName = null,
      OllamaConnectionMode? mode = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

      var uri = new Uri(endpoint.TrimEnd('/'));
      Mode = mode ?? (string.IsNullOrWhiteSpace(apiKey) ? OllamaConnectionMode.Local : OllamaConnectionMode.Cloud);
      var effectiveTimeout = timeout ?? (Mode == OllamaConnectionMode.Cloud ? DefaultCloudTimeout : DefaultLocalTimeout);

      var resolvedHttpClientName = !string.IsNullOrWhiteSpace(httpClientName)
         ? httpClientName
         : $"Delibera.Ollama.{Mode}";
      var resolvedPipelineName = !string.IsNullOrWhiteSpace(pipelineName)
         ? pipelineName
         : (Mode == OllamaConnectionMode.Cloud
            ? ResilienceOptions.CloudPipelineName
            : ResilienceOptions.LocalPipelineName);

      OllamaApiClient client;
      if (httpClientFactory is not null)
      {
         // Build the HttpClient through the factory — its underlying
         // handler pipeline already has the Polly resilience handler
         // attached via AddResilienceHandler. The provider itself doesn't
         // own this HttpClient (the factory does), so Dispose skips it.
         _httpClientFactory = httpClientFactory;
         _httpClientName = resolvedHttpClientName;
         _pipeline = resilienceProvider?.GetOperationPipeline(resolvedPipelineName);

         var http = _httpClientFactory.CreateClient(resolvedHttpClientName);
         http.BaseAddress ??= uri;
         http.Timeout = effectiveTimeout;
         if (Mode == OllamaConnectionMode.Cloud && !string.IsNullOrWhiteSpace(apiKey))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());

         client = new OllamaApiClient(http);
      }
      else
      {
         _httpClientFactory = null;
         _httpClientName = null;
         _pipeline = null;

         var httpClient = new HttpClient { BaseAddress = uri, Timeout = effectiveTimeout };
         if (Mode == OllamaConnectionMode.Cloud && !string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey.Trim()}");
         client = new OllamaApiClient(httpClient);
      }

      Client = client;
   }

   /// <summary>The connection mode this provider was configured with.</summary>
   public OllamaConnectionMode Mode { get; }

   /// <summary>
   ///    Provides access to the underlying OllamaSharp client (used by <see cref="OllamaEmbeddingProvider" />).
   /// </summary>
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

      // The chat operation is owned by OllamaSharp (it streams response chunks).
      // When a Polly v8 pipeline is configured we wrap the entire streaming
      // call: on a transient failure Polly cancels the current attempt and
      // starts a fresh one — OllamaSharp re-streams from the beginning. This
      // is the standard v8 pattern for non-idempotent-friendly streams, and
      // matches the v10.2 hand-rolled behaviour (which also restarted the
      // whole stream on a retry).
      string? captured = null;
      Func<CancellationToken, ValueTask> operation = async token =>
      {
         var sb = new StringBuilder();
         await foreach (var chunk in Client.ChatAsync(request, token))
         {
            if (chunk is not { Message.Content: { } content })
               continue;
            sb.Append(content);
         }

         var response = sb.ToString().Trim();
         if (string.IsNullOrWhiteSpace(response))
            throw new InvalidOperationException($"Empty response from model '{model}'.");
         captured = response;
      };

      try
      {
         if (_pipeline is null)
         {
            await operation(ct).ConfigureAwait(false);
            return captured ?? throw new InvalidOperationException($"Empty response from model '{model}'.");
         }

         // Build a context-scoped pipeline invocation. Polly v8 wraps
         // exceptions in a ResilienceException only when the pipeline
         // re-throws after exhausting retries; HttpRequestException and
         // TaskCanceledException pass straight through unchanged.
         var context = Polly.ResilienceContextPool.Shared.Get(ct);
         try
         {
            await _pipeline.ExecuteAsync<ChatState>(
               static async (ctx, state) =>
               {
                  // Pipeline's ShouldHandle predicate already filters which
                  // exceptions (HttpRequestException, TaskCanceledException)
                  // trigger a retry. Anything else propagates immediately.
                  await state.Op(ctx.CancellationToken).ConfigureAwait(false);
               },
               context,
               new ChatState(operation)).ConfigureAwait(false);
         }
         finally
         {
            Polly.ResilienceContextPool.Shared.Return(context);
         }

         return captured ?? throw new InvalidOperationException($"Empty response from model '{model}'.");
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
         throw;
      }
      catch (HttpRequestException ex)
      {
         throw new InvalidOperationException($"HTTP error talking to Ollama (model: {model}): {ex.Message}", ex);
      }
      catch (TimeoutException)
      {
         throw;
      }
   }

   /// <inheritdoc />
   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      // When constructed via IHttpClientFactory, the factory owns the HttpClient.
      // When constructed standalone, Client (which owns the HttpClient) is disposed here.
      if (_httpClientFactory is null)
         Client.Dispose();
   }

   /// <summary>Creates a provider for a local Ollama server (e.g. <c>http://localhost:11434</c>).</summary>
   public static OllamaProvider ForLocal(string endpoint, TimeSpan? timeout = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      return new OllamaProvider(endpoint, "", timeout, null, null,
         httpClientName: null, pipelineName: null,
         mode: OllamaConnectionMode.Local);
   }

   /// <summary>Creates a provider for Ollama Cloud (e.g. <c>https://api.ollama.com</c>) with an API key.</summary>
   public static OllamaProvider ForCloud(string endpoint, string apiKey, TimeSpan? timeout = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
      return new OllamaProvider(endpoint, apiKey, timeout, null, null,
         httpClientName: null, pipelineName: null,
         mode: OllamaConnectionMode.Cloud);
   }

   /// <summary>
   ///    Creates a DI-friendly local Ollama provider with handler pooling and the local retry pipeline.
   /// </summary>
   public static OllamaProvider ForLocal(
      string endpoint,
      IHttpClientFactory httpClientFactory,
      IDeliberaResiliencePipelineProvider resilienceProvider,
      string? httpClientName = null,
      string? pipelineName = null,
      TimeSpan? timeout = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      ArgumentNullException.ThrowIfNull(httpClientFactory);
      ArgumentNullException.ThrowIfNull(resilienceProvider);
      return new OllamaProvider(endpoint, "", timeout,
         httpClientFactory, resilienceProvider,
         httpClientName, pipelineName,
         mode: OllamaConnectionMode.Local);
   }

   /// <summary>
   ///    Creates a DI-friendly cloud Ollama provider with handler pooling and the cloud retry pipeline.
   /// </summary>
   public static OllamaProvider ForCloud(
      string endpoint,
      string apiKey,
      IHttpClientFactory httpClientFactory,
      IDeliberaResiliencePipelineProvider resilienceProvider,
      string? httpClientName = null,
      string? pipelineName = null,
      TimeSpan? timeout = null)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
      ArgumentNullException.ThrowIfNull(httpClientFactory);
      ArgumentNullException.ThrowIfNull(resilienceProvider);
      return new OllamaProvider(endpoint, apiKey, timeout,
         httpClientFactory, resilienceProvider,
         httpClientName, pipelineName,
         mode: OllamaConnectionMode.Cloud);
   }

   /// <summary>
   ///    Exposes the underlying OllamaSharp client as a Microsoft.Extensions.AI
   ///    <see cref="Microsoft.Extensions.AI.IChatClient" />.
   /// </summary>
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

   private sealed record ChatState(Func<CancellationToken, ValueTask> Op);
}
