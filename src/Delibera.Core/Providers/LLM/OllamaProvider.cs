using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Resilience;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using Polly;
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
///       Use <c>ForLocal</c> for a local server (e.g. <c>http://localhost:11434</c>) and
///       <c>ForCloud</c> for Ollama Cloud (e.g. <c>https://api.ollama.com</c>) with an API key.
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
public sealed partial class OllamaProvider : ILLMProvider
{
   private static readonly TimeSpan DefaultCloudTimeout = TimeSpan.FromMinutes(5);
   private static readonly TimeSpan DefaultLocalTimeout = TimeSpan.FromMinutes(10);

   private readonly IHttpClientFactory? _httpClientFactory;
   private readonly string? _httpClientName;
   private readonly int _maxOutputTokens;
   private readonly bool _enableThinking;
   private readonly bool _retryOnBudgetExhaustion;
   private readonly ResiliencePipeline? _pipeline;
   private bool _disposed;

   /// <summary>
   ///    Creates an Ollama provider. The mode is inferred from <paramref name="apiKey" />: non-empty selects cloud.
   ///    <para>
   ///       <paramref name="maxOutputTokens" /> (default <c>-1</c> = infinite generation) overrides the
   ///       OllamaSharp-<c>NumPredict</c> default of <c>128</c>, which truncates long responses (chairman
   ///       council verdicts, schema discovery, summaries) mid-JSON and breaks downstream parsers. Pass
   ///       a positive value to cap output per call, or <c>-1</c> to let the model/provider ceiling apply.
   ///    </para>
   /// </summary>
   public OllamaProvider(string endpoint, string apiKey = "", TimeSpan? timeout = null,
      int maxOutputTokens = -1)
      : this(endpoint, apiKey, timeout, null, null,
         null, null,
         string.IsNullOrWhiteSpace(apiKey)
            ? OllamaConnectionMode.Local
            : OllamaConnectionMode.Cloud,
         maxOutputTokens)
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
   /// <param name="maxOutputTokens">
   ///    Maximum number of tokens to predict per generation. Default <c>-1</c> = infinite generation
   ///    (the model/provider ceiling applies); overrides the OllamaSharp default of <c>128</c> which
   ///    truncates long responses mid-JSON. Pass a positive value to cap output per call.
   /// </param>
   /// <param name="enableThinking">
   ///   Sends <c>think</c> explicitly to the model. Reasoning models (gpt-oss, glm, deepseek)
   ///   think by default, and the reasoning tokens come out of the same generation budget as the
   ///   answer. When <c>false</c> the request carries <c>think: false</c>, so the whole budget is
   ///   available for the answer. Set <c>true</c> only when you actually want the reasoning and
   ///   have sized <paramref name="maxOutputTokens" /> to absorb it.
   /// </param>
   /// <param name="retryOnBudgetExhaustion">
   ///   When a reasoning model burns the entire generation budget thinking and is cut off before
   ///   emitting any answer (<c>done_reason: length</c> with zero content), retry once with a
   ///   larger budget. Default <c>true</c>. Disable it to get the failure immediately.
   /// </param>
   public OllamaProvider(
      string endpoint,
      string apiKey,
      TimeSpan? timeout,
      IHttpClientFactory? httpClientFactory,
      IDeliberaResiliencePipelineProvider? resilienceProvider,
      string? httpClientName = null,
      string? pipelineName = null,
      OllamaConnectionMode? mode = null,
      int maxOutputTokens = -1,
      bool enableThinking = false,
      bool retryOnBudgetExhaustion = true)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

      var uri = new Uri(endpoint.TrimEnd('/'));
      Mode = mode ??
             (string.IsNullOrWhiteSpace(apiKey)
                ? OllamaConnectionMode.Local
                : OllamaConnectionMode.Cloud);
      var effectiveTimeout = timeout ??
                             (Mode == OllamaConnectionMode.Cloud
                                ? DefaultCloudTimeout
                                : DefaultLocalTimeout);

      var resolvedHttpClientName = !string.IsNullOrWhiteSpace(httpClientName)
         ? httpClientName
         : $"Delibera.Ollama.{Mode}";
      var resolvedPipelineName = !string.IsNullOrWhiteSpace(pipelineName)
         ? pipelineName
         : Mode == OllamaConnectionMode.Cloud
            ? ResilienceOptions.CloudPipelineName
            : ResilienceOptions.LocalPipelineName;

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
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

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
      _maxOutputTokens = maxOutputTokens;
      _enableThinking = enableThinking;
      _retryOnBudgetExhaustion = retryOnBudgetExhaustion;
   }

   /// <summary>One completed stream: the answer plus the signal needed to classify an empty one.</summary>
   private readonly record struct StreamAttempt(
      string Text,
      string? DoneReason,
      int ReasoningChars,
      long PromptTokens,
      long EvalTokens)
   {
      /// <summary>
      ///   The generation budget ran out before any answer was produced. On a reasoning model
      ///   this is the signature of "the whole budget went into thinking": content is empty,
      ///   <c>done_reason</c> is <c>length</c>, and the reasoning field is non-empty.
      /// </summary>
      public bool ExhaustedBudget =>
         string.IsNullOrEmpty(Text) && ReasoningChars > 0
         && string.Equals(DoneReason, "length", StringComparison.OrdinalIgnoreCase);
   }

   /// <summary>
   ///   Streams one request, accumulating the answer separately from the reasoning text.
   /// </summary>
   /// <remarks>
   ///   <see cref="ChatRequest.Think" /> being false does not stop a model that thinks anyway
   ///   from emitting reasoning, so <see cref="Message.Thinking" /> is counted rather than
   ///   assumed absent. It is what distinguishes "budget spent on reasoning" from a genuinely
   ///   empty response, and the two need different handling.
   /// </remarks>
   private async Task<StreamAttempt> StreamOnceAsync(ChatRequest request, CancellationToken token)
   {
      var sb = new StringBuilder();
      var reasoningChars = 0;

      var done = await Client.ChatAsync(request, token)
         .StreamToEndAsync(chunk =>
         {
            if (chunk?.Message is not { } message)
               return;

            if (message.Content is { Length: > 0 } content)
               sb.Append(content);

            if (message.Thinking is { Length: > 0 } thought)
               reasoningChars += thought.Length;
         })
         .ConfigureAwait(false);

      return new StreamAttempt(
         sb.ToString().Trim(),
         done?.DoneReason,
         reasoningChars,
         done?.PromptEvalCount ?? 0,
         done?.EvalCount ?? 0);
   }

   /// <summary>
   ///   Picks the budget for the budget-exhaustion retry: double what the failed attempt was
   ///   allowed, or fall back to the prompt size plus a generous allowance when the caller left
   ///   the budget uncapped.
   /// </summary>
   private int NextBudget(long promptTokens, long evalTokens)
   {
      if (_maxOutputTokens > 0)
         return _maxOutputTokens * 2;

      // -1 means "unlimited" in the library, but the model still stops at its own ceiling and
      // reports a length cut-off; a concrete retry budget is what makes the retry meaningful.
      var spent = evalTokens > 0 ? (int)Math.Min(evalTokens, int.MaxValue) : 1024;
      var floor = promptTokens > 0 ? (int)Math.Min(promptTokens, int.MaxValue) : 0;
      return Math.Clamp(Math.Max(spent * 2, floor + 1024), 2048, 32_768);
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
         await Client.ListLocalModelsAsync(ct).ConfigureAwait(false);
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
         var models = await Client.ListLocalModelsAsync(ct).ConfigureAwait(false);
         return models.Select(m => m.Name).ToList().AsReadOnly();
      }
      catch (Exception ex)
      {
         throw new InvalidOperationException($"Failed to list Ollama models: {ex.Message}", ex);
      }
   }

   /// <inheritdoc />
   public async Task<ModelCapabilities> GetModelCapabilitiesAsync(string model, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(model);

      try
      {
         // Use Ollama's /api/show endpoint to get model metadata including Modelfile parameters.
         var response = await Client.ShowModelAsync(model, ct).ConfigureAwait(false);

         // 1. Try to extract num_ctx from the Modelfile parameters string.
         var contextWindow = ExtractContextWindowFromParameters(response.Parameters);

         // 2. Fall back to the static registry if the API didn't report a context window.
         contextWindow ??= ModelContextWindowRegistry.GetContextWindow(model);

         // 3. Determine model family from the model info.
         var family = response.Info?.Architecture ?? response.Details?.Family;

         // 4. Check capabilities for vision support.
         var supportsVision = response.Capabilities is not null &&
                              response.Capabilities.Any(c => c.Contains("vision", StringComparison.OrdinalIgnoreCase));

         return new ModelCapabilities
         {
            ModelName = model,
            ContextWindowTokens = contextWindow,
            Family = family,
            SupportsVision = supportsVision,
            SupportsTools = false // Ollama tool support is model-dependent; default to false.
         };
      }
      catch (Exception ex)
      {
         // If the API call fails, fall back to the static registry.
         Debug.WriteLine(
            $"OllamaProvider: failed to get capabilities for '{model}': {ex.Message}");

         var window = ModelContextWindowRegistry.GetContextWindow(model);
         return window is not null
            ? new ModelCapabilities { ModelName = model, ContextWindowTokens = window }
            : ModelCapabilities.Unknown(model);
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
         // NumPredict: the provider ctor's maxOutputTokens caps output per call.
         //
         // A non-positive maxOutputTokens means "no cap" and is sent as a *null* field, which
         // OllamaSharp omits from the request so the server applies its own default (unlimited).
         // Sending -1 explicitly does NOT work: Ollama Cloud rejects the request outright with
         // "max_tokens must be positive, got: -1", so the previous -1 default made every call
         // against Ollama Cloud fail before a single token was generated. Omitting the field also
         // avoids OllamaSharp's own 128-token default, which truncated long responses
         // (chairman verdicts, schema discovery, summaries) mid-JSON.
         //
         // NumCtx is intentionally left unset — Ollama reads the native context window from
         // /api/show Modelfile (see GetModelCapabilitiesAsync), so hardcoding 8192 would
         // shrink the context for large models (gpt-oss:120b-cloud = 128K, yandexgpt-5-pro = 32K).
         Options = new RequestOptions
         {
            Temperature = temperature,
            NumPredict = _maxOutputTokens > 0 ? _maxOutputTokens : null
         },
         // Sent explicitly: left unset, a reasoning model spends the shared generation budget on
         // reasoning and can be cut off before writing a single character of answer.
         Think = _enableThinking
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
         var attempt = await StreamOnceAsync(request, token).ConfigureAwait(false);

         // Budget exhaustion is not a transient failure: a reasoning model cut off at the same
         // num_predict on the next attempt fails identically, and Polly's cloud pipeline only
         // covers transport statuses. Retry once with a larger budget instead.
         if (_retryOnBudgetExhaustion && attempt.ExhaustedBudget)
         {
            request.Options.NumPredict = NextBudget(attempt.PromptTokens, attempt.EvalTokens);
            attempt = await StreamOnceAsync(request, token).ConfigureAwait(false);
         }

         if (string.IsNullOrWhiteSpace(attempt.Text))
            throw new OllamaEmptyResponseException(model, attempt.DoneReason, attempt.ReasoningChars);

         captured = attempt.Text;
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
         var context = ResilienceContextPool.Shared.Get(ct);
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
            ResilienceContextPool.Shared.Return(context);
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

   /// <summary>
   ///    Extracts the <c>num_ctx</c> parameter from an Ollama Modelfile parameters string.
   ///    The parameters string looks like:
   ///    <c>num_keep 24\nstop "&lt;|start_header_id|&gt;"\nnum_ctx 131072\n...</c>
   /// </summary>
   /// <summary>
   ///    Matches the <c>num_ctx &lt;number&gt;</c> capability Ollama reports in a model's
   ///    parameter blob. Compile-time literal, so it is source-generated rather than built
   ///    and cached on first use.
   /// </summary>
   [GeneratedRegex(@"num_ctx\s+(\d+)", RegexOptions.IgnoreCase)]
   private static partial Regex NumCtxRegex();

   private static int? ExtractContextWindowFromParameters(string? parameters)
   {
      if (string.IsNullOrWhiteSpace(parameters))
         return null;

      // Match "num_ctx <number>" in the parameters string.
      var match = NumCtxRegex().Match(parameters);
      if (match.Success && int.TryParse(match.Groups[1].Value, out var ctx))
         return ctx;

      return null;
   }

   /// <summary>Creates a provider for a local Ollama server (e.g. <c>http://localhost:11434</c>).</summary>
   /// <param name="endpoint">Ollama endpoint URL.</param>
   /// <param name="timeout">HTTP timeout (null = local default).</param>
   /// <param name="maxOutputTokens">Per-call output token cap; <c>-1</c> = infinite generation (default).</param>
   public static OllamaProvider ForLocal(string endpoint, TimeSpan? timeout = null,
      int maxOutputTokens = -1)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      return new OllamaProvider(endpoint, "", timeout, null, null,
         null, null,
         OllamaConnectionMode.Local,
         maxOutputTokens);
   }

   /// <summary>Creates a provider for Ollama Cloud (e.g. <c>https://api.ollama.com</c>) with an API key.</summary>
   /// <param name="endpoint">Ollama endpoint URL.</param>
   /// <param name="apiKey">Ollama Cloud API key.</param>
   /// <param name="timeout">HTTP timeout (null = cloud default).</param>
   /// <param name="maxOutputTokens">Per-call output token cap; <c>-1</c> = infinite generation (default).</param>
   public static OllamaProvider ForCloud(string endpoint, string apiKey, TimeSpan? timeout = null,
      int maxOutputTokens = -1)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
      return new OllamaProvider(endpoint, apiKey, timeout, null, null,
         null, null,
         OllamaConnectionMode.Cloud,
         maxOutputTokens);
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
      TimeSpan? timeout = null,
      int maxOutputTokens = -1)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      ArgumentNullException.ThrowIfNull(httpClientFactory);
      ArgumentNullException.ThrowIfNull(resilienceProvider);
      return new OllamaProvider(endpoint, "", timeout,
         httpClientFactory, resilienceProvider,
         httpClientName, pipelineName,
         OllamaConnectionMode.Local,
         maxOutputTokens);
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
      TimeSpan? timeout = null,
      int maxOutputTokens = -1)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
      ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
      ArgumentNullException.ThrowIfNull(httpClientFactory);
      ArgumentNullException.ThrowIfNull(resilienceProvider);
      return new OllamaProvider(endpoint, apiKey, timeout,
         httpClientFactory, resilienceProvider,
         httpClientName, pipelineName,
         OllamaConnectionMode.Cloud,
         maxOutputTokens);
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
