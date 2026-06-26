using Delibera.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;

namespace Delibera.Core.Resilience;

/// <summary>
///    Convenience extensions for registering Polly v8 resilience pipelines
///    with the Delibera DI container.
/// </summary>
public static class DeliberaResilienceServiceCollectionExtensions
{
   /// <summary>
   ///    Registers the <see cref="IDeliberaResiliencePipelineProvider" /> singleton
   ///    and the consumer-supplied named pipelines alongside it.
   /// </summary>
   /// <remarks>
   ///    Internal-use overload — the public surface is <c>ServiceCollectionExtensions.AddDeliberaResilience</c>.
   /// </remarks>
   public static IServiceCollection AddDeliberaResilienceCore(
      this IServiceCollection services,
      IEnumerable<KeyValuePair<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>>>? customPipelines = null)
   {
      ArgumentNullException.ThrowIfNull(services);

      // Eagerly register the mutable collection so each
      // AddDeliberaResiliencePipeline call can mutate the same instance.
      services.TryAddSingleton<DeliberaResiliencePipelineCollection>();

      services.TryAddSingleton<IDeliberaResiliencePipelineProvider>(sp =>
      {
         var monitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ResilienceOptions>>();
         var collection = sp.GetRequiredService<DeliberaResiliencePipelineCollection>();
         var sequences = new List<KeyValuePair<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>>>(collection.Snapshot());
         if (customPipelines is not null)
            foreach (var kvp in customPipelines)
               sequences.Add(kvp);
         return new DeliberaResiliencePipelineProvider(monitor, sequences);
      });

      return services;
   }

   /// <summary>
   ///    Registers a single Polly v8 pipeline under <paramref name="name" />.
   /// </summary>
   /// <param name="services">The service collection.</param>
   /// <param name="name">
   ///    Pipeline key. When the same name matches one of the built-in keys
   ///    (<see cref="ResilienceOptions.LocalPipelineName" /> etc.) the
   ///    consumer pipeline replaces the built-in for that key.
   /// </param>
   /// <param name="build">
   ///    Factory delegate that configures a <see cref="ResiliencePipelineBuilder{TResult}" /> and
   ///    returns the resulting pipeline.
   /// </param>
   /// <returns>The service collection for chaining.</returns>
   public static IServiceCollection AddDeliberaResiliencePipeline(
      this IServiceCollection services,
      string name,
      Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>> build)
   {
      ArgumentNullException.ThrowIfNull(services);
      ArgumentException.ThrowIfNullOrWhiteSpace(name);
      ArgumentNullException.ThrowIfNull(build);

      // Run during service-provider build so the collection singleton has
      // been resolved and can be mutated. We use a marker singleton of
      // <see cref="DeliberaResiliencePipelineRegistration" />; its factory
      // receives the collection and appends to it.
      services.AddSingleton(sp =>
      {
         var collection = sp.GetRequiredService<DeliberaResiliencePipelineCollection>();
         collection.Add(name, build);
         return new DeliberaResiliencePipelineRegistration(name);
      });
      return services;
   }
}

/// <summary>
///    Marker singleton returned by <see cref="DeliberaResilienceServiceCollectionExtensions.AddDeliberaResiliencePipeline" />.
///    Side-effect (mutating the shared collection) is the goal; the instance is discarded.
/// </summary>
internal sealed record DeliberaResiliencePipelineRegistration(string Name);

/// <summary>
///    Internal mutable collection of consumer-registered resilience pipelines.
///    Held as a singleton so multiple <see cref="DeliberaResilienceServiceCollectionExtensions.AddDeliberaResiliencePipeline" />
///    calls accumulate their entries before the provider factory consumes them.
/// </summary>
internal sealed class DeliberaResiliencePipelineCollection
{
   private readonly object _gate = new();
   private readonly List<KeyValuePair<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>>> _entries = [];

   public void Add(string name, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>> build)
   {
      lock (_gate)
         _entries.Add(new(name, build));
   }

   public IReadOnlyList<KeyValuePair<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>>> Snapshot()
   {
      lock (_gate)
         return _entries.ToList();
   }
}