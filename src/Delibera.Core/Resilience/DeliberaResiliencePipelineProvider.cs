using Delibera.Core.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Delibera.Core.Resilience;

/// <summary>
///    Central registry that builds the named Polly v8
///    <see cref="ResiliencePipeline{TResult}" /> instances consumed by every
///    HTTP-backed Delibera provider.
/// </summary>
/// <remarks>
///    <para>
///       The factory reads its configuration from
///       <see cref="ResilienceOptions" /> (typically bound from the
///       <c>Delibera:Resilience</c> configuration section). It exposes
///       three pipelines out of the box:
///       <list type="bullet">
///          <item><see cref="ResilienceOptions.LocalPipelineName" /> — retries only connection-level failures.</item>
///          <item><see cref="ResilienceOptions.CloudPipelineName" /> — retries transient HTTP responses (429, 524, 5xx) plus timeouts.</item>
///          <item><see cref="ResilienceOptions.DefaultPipelineName" /> — convenience alias for the cloud pipeline (the more permissive of the two).</item>
///       </list>
///    </para>
///    <para>
///       Consumers can register additional named pipelines through
///       <c>services.AddDeliberaResiliencePipeline("MyKey", b => …)</c>; the
///       factory merges them into its lookup table on first use.
///    </para>
/// </remarks>
public interface IDeliberaResiliencePipelineProvider
{
   /// <summary>
   ///    Returns the Polly v8 HTTP-level pipeline registered under <paramref name="name" />.
   ///    Falls back to the default pipeline when <paramref name="name" /> is
   ///    <c>null</c>, empty, or unknown.
   /// </summary>
   /// <param name="name">
   ///    The pipeline key (one of the constants on <see cref="ResilienceOptions" />,
   ///    or a custom name registered via
   ///    <c>AddDeliberaResiliencePipeline</c>).
   /// </param>
   /// <returns>A non-<c>null</c> pipeline. Never throws on missing keys.</returns>
   ResiliencePipeline<HttpResponseMessage> GetPipeline(string? name);

   /// <summary>
   ///    Returns a Polly v8 non-generic operation-level pipeline (for use-cases where the
   ///    operation is a unit of work that doesn't produce an <see cref="HttpResponseMessage" />
   ///    result — e.g. <c>OllamaProvider.ChatAsync</c> streams text from OllamaSharp).
   ///    Shares the same configuration as <see cref="GetPipeline" /> but with no result-typed
   ///    retry predicates — only exception types (<see cref="HttpRequestException" />,
   ///    <see cref="TaskCanceledException" />) trigger retries.
   /// </summary>
   /// <param name="name">Pipeline key.</param>
   /// <returns>A non-generic <see cref="ResiliencePipeline" /> or <c>null</c> when disabled.</returns>
   ResiliencePipeline? GetOperationPipeline(string? name);
}

/// <summary>
///    Default implementation of <see cref="IDeliberaResiliencePipelineProvider" />.
/// </summary>
public sealed class DeliberaResiliencePipelineProvider : IDeliberaResiliencePipelineProvider
{
   private readonly IOptionsMonitor<ResilienceOptions> _options;
   private readonly Dictionary<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>> _customBuilders;
   private readonly Dictionary<string, ResiliencePipeline<HttpResponseMessage>> _built;
   private readonly Dictionary<string, ResiliencePipeline> _builtOperation;
   private readonly object _gate = new();

   /// <summary>Builds the provider from DI options + custom pipeline factories.</summary>
   /// <param name="options">Bound resilience configuration.</param>
   /// <param name="customPipelines">Optional consumer-registered pipelines (may be <c>null</c>).</param>
   public DeliberaResiliencePipelineProvider(
      IOptionsMonitor<ResilienceOptions> options,
      IEnumerable<KeyValuePair<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>>>? customPipelines = null)
   {
      ArgumentNullException.ThrowIfNull(options);
      _options = options;
      _customBuilders = new Dictionary<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>>(StringComparer.OrdinalIgnoreCase);
      _built = new Dictionary<string, ResiliencePipeline<HttpResponseMessage>>(StringComparer.OrdinalIgnoreCase);
      _builtOperation = new Dictionary<string, ResiliencePipeline>(StringComparer.OrdinalIgnoreCase);
      if (customPipelines is not null)
         foreach (var (name, build) in customPipelines)
            if (!string.IsNullOrWhiteSpace(name) && build is not null)
               _customBuilders[name] = build;
   }

   /// <inheritdoc />
   public ResiliencePipeline<HttpResponseMessage> GetPipeline(string? name)
   {
      var key = string.IsNullOrWhiteSpace(name)
         ? ResilienceOptions.DefaultPipelineName
         : name;

      if (_customBuilders.ContainsKey(key))
         return GetOrBuildCustom(key);

      return key switch
      {
         ResilienceOptions.LocalPipelineName => GetOrBuildBuilt(ResilienceOptions.LocalPipelineName, BuildLocal),
         ResilienceOptions.CloudPipelineName => GetOrBuildBuilt(ResilienceOptions.CloudPipelineName, BuildCloud),
         _ => GetOrBuildBuilt(ResilienceOptions.DefaultPipelineName, BuildCloud)
      };
   }

   private ResiliencePipeline<HttpResponseMessage> GetOrBuildCustom(string key)
   {
      lock (_gate)
      {
         if (_built.TryGetValue(key, out var cached))
            return cached;
         var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
         var pipeline = _customBuilders[key](builder);
         _built[key] = pipeline;
         return pipeline;
      }
   }

   private ResiliencePipeline<HttpResponseMessage> GetOrBuildBuilt(string key, Func<ResilienceOptions, ResiliencePipeline<HttpResponseMessage>> build)
   {
      lock (_gate)
      {
         if (_built.TryGetValue(key, out var cached))
            return cached;
         var opts = _options.Get(key);
         var pipeline = build(opts);
         _built[key] = pipeline;
         return pipeline;
      }
   }

   private static ResiliencePipeline<HttpResponseMessage> BuildLocal(ResilienceOptions opts)
   {
      if (!opts.Enabled)
         return ResiliencePipeline<HttpResponseMessage>.Empty;

      var retry = new RetryStrategyOptions<HttpResponseMessage>
      {
         MaxRetryAttempts = Math.Max(0, opts.MaxRetryAttempts),
         Delay = opts.BaseDelay,
         MaxDelay = opts.MaxDelay,
         BackoffType = ParseBackoffType(opts.BackoffType),
         UseJitter = opts.UseJitter,
         // Local pipeline: retry only when the request never produced a
         // response (connection-level failure or socket timeout).
         ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
      };

      return new ResiliencePipelineBuilder<HttpResponseMessage>()
         .AddRetry(retry)
         .Build();
   }

   private ResiliencePipeline<HttpResponseMessage> BuildCloud(ResilienceOptions opts)
   {
      if (!opts.Enabled)
         return ResiliencePipeline<HttpResponseMessage>.Empty;

      var set = new HashSet<int>(opts.RetryableStatusCodes is { Length: > 0 }
         ? opts.RetryableStatusCodes
         : DefaultCloudStatusCodes);

      var retry = new RetryStrategyOptions<HttpResponseMessage>
      {
         MaxRetryAttempts = Math.Max(0, opts.MaxRetryAttempts),
         Delay = opts.BaseDelay,
         MaxDelay = opts.MaxDelay,
         BackoffType = ParseBackoffType(opts.BackoffType),
         UseJitter = opts.UseJitter,
         ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
            .HandleResult(r => set.Contains((int)r.StatusCode))
      };

      return new ResiliencePipelineBuilder<HttpResponseMessage>()
         .AddRetry(retry)
         .Build();
   }

   private static readonly int[] DefaultCloudStatusCodes = [408, 429, 500, 502, 503, 504, 524];

   /// <inheritdoc />
   public ResiliencePipeline? GetOperationPipeline(string? name)
   {
      var key = string.IsNullOrWhiteSpace(name) ? ResilienceOptions.DefaultPipelineName : name;
      lock (_gate)
      {
         if (_builtOperation.TryGetValue(key, out var cached))
            return cached;
         var opts = _options.Get(key);
         var pipeline = BuildOperationPipeline(opts);
         _builtOperation[key] = pipeline;
         return pipeline;
      }
   }

   private static ResiliencePipeline BuildOperationPipeline(ResilienceOptions opts)
   {
      if (!opts.Enabled)
         return ResiliencePipeline.Empty;

      var retry = new RetryStrategyOptions
      {
         MaxRetryAttempts = Math.Max(0, opts.MaxRetryAttempts),
         Delay = opts.BaseDelay,
         MaxDelay = opts.MaxDelay,
         BackoffType = ParseBackoffType(opts.BackoffType),
         UseJitter = opts.UseJitter,
         ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
      };

      return new ResiliencePipelineBuilder()
         .AddRetry(retry)
         .Build();
   }

   private static DelayBackoffType ParseBackoffType(string value)
   {
      if (string.IsNullOrWhiteSpace(value))
         return DelayBackoffType.Exponential;
      return value.Trim().ToLowerInvariant() switch
      {
         "constant" => DelayBackoffType.Constant,
         "linear" => DelayBackoffType.Linear,
         _ => DelayBackoffType.Exponential
      };
   }
}