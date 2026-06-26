using Delibera.Core.DependencyInjection;
using Delibera.Core.Interfaces;
using Delibera.Core.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates Delibera v10.2.2 Polly v8 resilience integration:
///    registers <see cref="IDeliberaResiliencePipelineProvider" />, named HttpClients
///    (Ollama.Local, Ollama.Cloud, YandexGPT) with HttpRetryStrategyOptions attached via
///    Microsoft.Extensions.Http.Resilience, and constructs an Ollama provider that uses
///    the named client through IHttpClientFactory. Transient failures (connection drops,
///    HTTP 429/5xx, Cloudflare 524 origin timeouts) are retried by the configured
///    Polly v8 pipeline.
/// </summary>
public static class ResilienceExample
{
   /// <summary>Runs the resilience demo end-to-end.</summary>
   public static async Task RunAsync()
   {
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  🛡️  Delibera — Polly v8 Resilience Example (v10.2.2)");
      Console.WriteLine("═══════════════════════════════════════════\n");

      var configuration = new ConfigurationBuilder()
         .SetBasePath(Directory.GetCurrentDirectory())
         .AddJsonFile("appsettings.json", optional: true)
         .Build();

      var services = new ServiceCollection();

      // 1. Bind CouncilOptions (so the Resilience section flows in too).
      services.AddDelibera(configuration);

      // 2. Wire resilience — registers IDeliberaResiliencePipelineProvider and the
      //    named HttpClients Delibera.Ollama.Local, Delibera.Ollama.Cloud, Delibera.YandexGPT.
      services.AddDeliberaResilience(opts =>
      {
         opts.MaxRetryAttempts = 4;
         opts.BaseDelay = TimeSpan.FromSeconds(1);
         opts.MaxDelay = TimeSpan.FromSeconds(20);
         opts.UseJitter = true;
         opts.RetryableStatusCodes = [408, 429, 500, 502, 503, 504, 524];
      });

      // 3. Register a custom named pipeline — visible in the pipeline lookup
      //    and reusable from any provider that asks for "Delibera.Custom".
      services.AddDeliberaResiliencePipeline("Delibera.Custom", b => b
         .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
         {
            Name = "Delibera.Custom",
            MaxRetryAttempts = 6,
            Delay = TimeSpan.FromMilliseconds(500),
            UseJitter = true,
            BackoffType = Polly.DelayBackoffType.Exponential,
            ShouldHandle = new Polly.PredicateBuilder<HttpResponseMessage>()
               .Handle<HttpRequestException>()
         })
         .Build());

      var sp = services.BuildServiceProvider();

      // 4. Resolve the pipeline registry and confirm each named pipeline is available.
      var registry = sp.GetRequiredService<IDeliberaResiliencePipelineProvider>();
      Console.WriteLine("✅ Pipelines registered:");
      foreach (var name in new[] {
         ResilienceOptions.DefaultPipelineName,
         ResilienceOptions.LocalPipelineName,
         ResilienceOptions.CloudPipelineName,
         "Delibera.Custom"
      })
      {
         var p = registry.GetPipeline(name);
         Console.WriteLine($"   • {name,-30} → resolved: {!ReferenceEquals(p, Polly.ResiliencePipeline<HttpResponseMessage>.Empty)}");
      }

      // 5. Resolve the named HttpClient.
      var factory = sp.GetRequiredService<IHttpClientFactory>();
      var http = factory.CreateClient("Delibera.Ollama.Local");
      Console.WriteLine($"\n✅ IHttpClientFactory produced HttpClient: {http.GetType().Name}");

      // 6. Demonstrate the cloud pipeline config (no actual request to keep the demo offline).
      var optsMonitor = sp.GetRequiredService<IOptionsMonitor<ResilienceOptions>>();
      var cloudOpts = optsMonitor.Get(ResilienceOptions.CloudPipelineName);
      Console.WriteLine($"\n✅ Cloud pipeline config snapshot:");
      Console.WriteLine($"   MaxRetryAttempts: {cloudOpts.MaxRetryAttempts}");
      Console.WriteLine($"   BaseDelay:        {cloudOpts.BaseDelay}");
      Console.WriteLine($"   MaxDelay:         {cloudOpts.MaxDelay}");
      Console.WriteLine($"   UseJitter:        {cloudOpts.UseJitter}");
      Console.WriteLine($"   RetryableCodes:   [{string.Join(", ", cloudOpts.RetryableStatusCodes)}]");

      Console.WriteLine("\n💡 This demo runs entirely offline. To exercise the actual retry path,");
      Console.WriteLine("   point an Ollama client at a real endpoint and temporarily kill the server.");

      await Task.CompletedTask;
   }
}
