using Delibera.Core.Chunking;
using Delibera.Core.Compression;
using Delibera.Core.Council;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;
using Delibera.Core.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Delibera.Core.DependencyInjection;

/// <summary>
///    Extension methods for registering Delibera services with <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
   /// <param name="services">The service collection.</param>
   extension(IServiceCollection services)
   {
      /// <summary>
      ///    Registers core Delibera services with default options (no IHttpClientFactory, no resilience).
      /// </summary>
      /// <returns>The service collection for chaining.</returns>
      /// <remarks>
      ///    Registers:
      ///    <list type="bullet">
      ///       <item><see cref="ILLMProviderFactory" /> → <see cref="ProviderFactory" /> (singleton)</item>
      ///       <item><see cref="IRagProviderFactory" /> → <see cref="RagProviderFactory" /> (singleton)</item>
      ///       <item><see cref="ICompressionFactory" /> → <see cref="CompressionService" /> (singleton)</item>
      ///       <item><see cref="ICouncilBuilder" /> → <see cref="CouncilBuilder" /> (transient)</item>
      ///    </list>
      ///    To enable Polly v8 retry pipelines call <c>AddDeliberaResilience(IServiceCollection)</c> after this method.
      /// </remarks>
      public IServiceCollection AddDelibera()
      {
         services.TryAddSingleton<ILLMProviderFactory, ProviderFactory>();
         services.TryAddSingleton<IRagProviderFactory, RagProviderFactory>();
         services.TryAddSingleton<ICompressionFactory, CompressionService>();
         services.TryAddTransient<ICouncilBuilder, CouncilBuilder>();

         return services;
      }

      /// <summary>
      ///    Registers core Delibera services and binds <see cref="CouncilOptions" /> from configuration.
      /// </summary>
      public IServiceCollection AddDelibera(IConfiguration configuration,
         string sectionName = CouncilOptions.SectionName)
      {
         services.AddDelibera();

         var section = configuration.GetSection(sectionName);
         if (section.Exists()) services.Configure<CouncilOptions>(section);

         // ResilienceOptions is a sub-section; bind it independently so
         // IOptionsMonitor<ResilienceOptions> gets a typed configuration that
         // AddDeliberaResilience can read.
         var resilienceSection = configuration.GetSection($"{sectionName}:Resilience");
         if (resilienceSection.Exists())
            services.Configure<ResilienceOptions>(resilienceSection);

         return services;
      }

      /// <summary>
      ///    Registers core Delibera services with a custom options configuration delegate.
      /// </summary>
      public IServiceCollection AddDelibera(Action<CouncilOptions> configureOptions)
      {
         ArgumentNullException.ThrowIfNull(configureOptions);
         services.AddDelibera();
         services.Configure(configureOptions);

         return services;
      }

      /// <summary>
      ///    Registers core Delibera services and wires the framework into the host's
      ///    <see cref="ILoggerFactory" />. A <see cref="CouncilBuilder" /> resolved from the
      ///    container is automatically decorated with a logger, so any debate started via DI
      ///    logs to the host's pipeline (console, file, OpenTelemetry, …) in addition to the
      ///    in-memory <see cref="ExecutionLog" /> collection.
      /// </summary>
      public IServiceCollection AddDelibera(IConfiguration configuration,
         ILoggerFactory loggerFactory,
         string sectionName = CouncilOptions.SectionName)
      {
         ArgumentNullException.ThrowIfNull(loggerFactory);
         services.AddDelibera(configuration, sectionName);
         services.TryAddSingleton(loggerFactory);

      // Replace the transient builder registration so every resolved ICouncilBuilder
      // gets a logger injected automatically and CouncilOptions applied from DI.
      // Consumers who build the executor themselves can still call WithLogger(...)
      // or WithOptions(...) explicitly to override.
      services.Replace(ServiceDescriptor.Transient<ICouncilBuilder>(sp =>
      {
         // Resolve CouncilOptions from DI if available.
         var options = sp.GetService<Microsoft.Extensions.Options.IOptions<CouncilOptions>>()?.Value;

         // Create builder — if options are available, pass them to the constructor
         // so all settings (strategy, rounds, temperature, compression, auto-chunking, etc.)
         // are applied automatically.
         var builder = options is not null
            ? new CouncilBuilder(options)
            : new CouncilBuilder();

         // Attach logger from DI.
         var lf = sp.GetService<ILoggerFactory>();
         if (lf is not null)
            builder.WithLogger(lf.CreateLogger("Delibera.Core.Council"));

         return builder;
      }));

         return services;
      }

      /// <summary>
      ///    Registers <see cref="IDeliberaResiliencePipelineProvider" /> together with named Polly v8
      ///    retry pipelines (<c>Delibera.Local</c>, <c>Delibera.Cloud</c>, <c>Delibera.Default</c>).
      ///    Also registers named <see cref="HttpClient" /> entries that wire each pipeline into the
      ///    HttpClient handler chain via the standard Microsoft.Extensions.Http.Resilience AddResilienceHandler extension.
      /// </summary>
      /// <param name="configure">Optional delegate to override <see cref="ResilienceOptions" /> defaults.</param>
      /// <returns>The service collection for chaining.</returns>
      /// <remarks>
      ///    Call this <em>after</em> <c>AddDelibera(...)</c> and after binding <see cref="CouncilOptions" />.
      ///    The named HttpClients exposed are:
      ///    <list type="bullet">
      ///       <item><c>Delibera.Ollama.Local</c> / <c>Delibera.Ollama.Cloud</c> — base address must be set by the caller.</item>
      ///       <item><c>Delibera.YandexGPT</c></item>
      ///       <item><c>Delibera.Mcp.{ServerName}</c> — registered lazily by the MCP factory.</item>
      ///    </list>
      /// </remarks>
      public IServiceCollection AddDeliberaResilience(Action<ResilienceOptions>? configure = null)
      {
         ArgumentNullException.ThrowIfNull(services);

         // Configure options if the caller supplied a delegate.
         if (configure is not null)
            services.Configure(configure);

         // Register the pipeline factory (and any consumer-supplied custom pipelines).
         services.AddDeliberaResilienceCore(customPipelines: null);

         // Register the three built-in HttpClients with Polly resilience handlers attached.
         AddNamedHttpClient(services, "Delibera.Ollama.Local", ResilienceOptions.LocalPipelineName);
         AddNamedHttpClient(services, "Delibera.Ollama.Cloud", ResilienceOptions.CloudPipelineName);
         AddNamedHttpClient(services, "Delibera.YandexGPT", ResilienceOptions.CloudPipelineName);

         return services;
      }

      /// <summary>
      ///    Registers a single named <see cref="HttpClient" /> whose handler chain is decorated with a
      ///    Polly v8 <see cref="Polly.ResiliencePipeline{TResult}" /> configured from
      ///    <see cref="ResilienceOptions" />.
      /// </summary>
      /// <param name="name">Logical client name (e.g. <c>"Delibera.YandexGPT"</c>).</param>
      /// <param name="pipelineName">
      ///    Pipeline key — currently used for telemetry only. The actual retry behaviour is
      ///    derived from <see cref="ResilienceOptions" /> at HttpClient creation time.
      /// </param>
      /// <param name="configure">Optional <see cref="HttpClient" /> configuration delegate.</param>
      /// <returns>The <see cref="IHttpClientBuilder" /> for further chaining.</returns>
      public IHttpClientBuilder AddDeliberaHttpClient(string name,
         string pipelineName = ResilienceOptions.DefaultPipelineName,
         Action<HttpClient>? configure = null)
      {
         ArgumentNullException.ThrowIfNull(services);
         ArgumentException.ThrowIfNullOrWhiteSpace(name);

         var builder = services.AddHttpClient(name, configure ?? (_ => { }));

         builder.AddResilienceHandler(pipelineName, (pipelineBuilder, context) =>
         {
            // Resolve the live ResilienceOptions snapshot so option changes are honoured.
            var monitor = context.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptionsMonitor<ResilienceOptions>>();
            var opts = monitor is not null
               ? (pipelineName == ResilienceOptions.LocalPipelineName || pipelineName == ResilienceOptions.CloudPipelineName
                  ? monitor.Get(ResilienceOptions.DefaultPipelineName)
                  : monitor.CurrentValue)
               : new ResilienceOptions();

            if (!opts.Enabled)
               return; // Empty pipeline = no retries.

            // The "Delibera.Local" pipeline retries only on connection-level failures;
            // everything else (Cloud, Default, custom) retries on the configured status codes.
            var statusCodes = pipelineName == ResilienceOptions.LocalPipelineName
               ? null
               : opts.RetryableStatusCodes is { Length: > 0 } ? opts.RetryableStatusCodes : null;

            var retry = new HttpRetryStrategyOptions
            {
               Name = pipelineName,
               MaxRetryAttempts = Math.Max(0, opts.MaxRetryAttempts),
               Delay = opts.BaseDelay,
               MaxDelay = opts.MaxDelay,
               BackoffType = ParseBackoffType(opts.BackoffType),
               UseJitter = opts.UseJitter
            };

            if (statusCodes is null)
            {
               retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                  .Handle<HttpRequestException>()
                  .Handle<TaskCanceledException>();
            }
            else
            {
               var set = new HashSet<int>(statusCodes);
               retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                  .Handle<HttpRequestException>()
                  .Handle<TaskCanceledException>()
                  .HandleResult(r => set.Contains((int)r.StatusCode));
            }

            pipelineBuilder.AddRetry(retry);
         });

         return builder;
      }
   }

   private static void AddNamedHttpClient(IServiceCollection services, string name, string pipelineName)
   {
      services.AddDeliberaHttpClient(name, pipelineName);
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

   extension(IServiceCollection services)
   {
      /// <summary>
      ///    Registers a Microsoft.Extensions.AI <see cref="IChatClient" /> and exposes it as a Delibera
      ///    <see cref="ILLMProvider" /> (<see cref="ChatClientLLMProvider" />).
      /// </summary>
      public IServiceCollection AddDeliberaChatClient(Func<IServiceProvider, IChatClient> chatClientFactory,
         string? providerName = null)
      {
         ArgumentNullException.ThrowIfNull(chatClientFactory);

         services.AddDelibera();
         services.TryAddSingleton(chatClientFactory);
         // The DI container owns the IChatClient lifetime, so the provider must not dispose it.
         services.TryAddSingleton<ILLMProvider>(sp =>
            new ChatClientLLMProvider(sp.GetRequiredService<IChatClient>(), providerName, false));

         return services;
      }

      /// <summary>
      ///    Registers an already-constructed Microsoft.Extensions.AI <see cref="IChatClient" /> and exposes it
      ///    as a Delibera <see cref="ILLMProvider" />.
      /// </summary>
      public IServiceCollection AddDeliberaChatClient(IChatClient chatClient,
         string? providerName = null)
      {
         ArgumentNullException.ThrowIfNull(chatClient);
         return services.AddDeliberaChatClient(_ => chatClient, providerName);
      }

      /// <summary>
      ///    Registers a Microsoft.Extensions.AI <see cref="IEmbeddingGenerator{TInput,TEmbedding}" /> and exposes
      ///    it as a Delibera <see cref="IEmbeddingProvider" /> (<see cref="EmbeddingGeneratorProvider" />).
      /// </summary>
      public IServiceCollection AddDeliberaEmbeddingGenerator(Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>> generatorFactory,
         string? modelName = null,
         int? vectorSize = null)
      {
         ArgumentNullException.ThrowIfNull(generatorFactory);

         services.AddDelibera();
         services.TryAddSingleton(generatorFactory);
         services.TryAddSingleton<IEmbeddingProvider>(sp =>
            new EmbeddingGeneratorProvider(
               sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
               modelName,
               vectorSize,
               false));

         return services;
      }
   }
}
