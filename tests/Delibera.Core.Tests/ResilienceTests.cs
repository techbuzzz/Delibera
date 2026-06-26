using Delibera.Core.DependencyInjection;
using Delibera.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace Delibera.Core.Tests;

/// <summary>
///    Unit tests for the Polly v8 resilience integration:
///    <see cref="ResilienceOptions" /> defaults,
///    <see cref="DeliberaResiliencePipelineProvider" /> behaviour, and
///    the <c>AddDeliberaResilience</c> DI extension.
/// </summary>
public sealed class ResilienceTests
{
   [Fact]
   public void ResilienceOptions_HasSensibleBuiltInDefaults()
   {
      var opts = new ResilienceOptions();

      opts.Enabled.Should().BeTrue();
      opts.MaxRetryAttempts.Should().Be(3);
      opts.BaseDelay.Should().Be(TimeSpan.FromSeconds(2));
      opts.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
      opts.UseJitter.Should().BeTrue();
      opts.BackoffType.Should().Be("Exponential");
      opts.RetryableStatusCodes.Should().BeEquivalentTo(new[] { 408, 429, 500, 502, 503, 504, 524 });
   }

   [Fact]
   public void ResilienceOptions_ExposesStablePipelineNames()
   {
      ResilienceOptions.DefaultPipelineName.Should().Be("Delibera.Default");
      ResilienceOptions.LocalPipelineName.Should().Be("Delibera.Local");
      ResilienceOptions.CloudPipelineName.Should().Be("Delibera.Cloud");
   }

   [Fact]
   public void DeliberaResiliencePipelineProvider_ReturnsEmptyPipeline_WhenDisabled()
   {
      var provider = new DeliberaResiliencePipelineProvider(
         Microsoft.Extensions.Options.Options.Create(new ResilienceOptions { Enabled = false }).AsMonitor());

      var p = provider.GetPipeline(ResilienceOptions.CloudPipelineName);
      ReferenceEquals(p, ResiliencePipeline<HttpResponseMessage>.Empty).Should().BeTrue();

      var op = provider.GetOperationPipeline(ResilienceOptions.CloudPipelineName);
      ReferenceEquals(op, ResiliencePipeline.Empty).Should().BeTrue();
   }

   [Fact]
   public void DeliberaResiliencePipelineProvider_ReturnsNonEmptyPipeline_WhenEnabled()
   {
      var provider = new DeliberaResiliencePipelineProvider(
         Microsoft.Extensions.Options.Options.Create(new ResilienceOptions()).AsMonitor());

      var cloud = provider.GetPipeline(ResilienceOptions.CloudPipelineName);
      ReferenceEquals(cloud, ResiliencePipeline<HttpResponseMessage>.Empty).Should().BeFalse();

      var local = provider.GetPipeline(ResilienceOptions.LocalPipelineName);
      ReferenceEquals(local, ResiliencePipeline<HttpResponseMessage>.Empty).Should().BeFalse();

      var op = provider.GetOperationPipeline(ResilienceOptions.CloudPipelineName);
      ReferenceEquals(op, ResiliencePipeline.Empty).Should().BeFalse();
   }

   [Fact]
   public void DeliberaResiliencePipelineProvider_FallsBackToDefault_WhenNameUnknown()
   {
      var provider = new DeliberaResiliencePipelineProvider(
         Microsoft.Extensions.Options.Options.Create(new ResilienceOptions()).AsMonitor());

      var resolved = provider.GetPipeline("non-existent-pipeline");
      var fallback = provider.GetPipeline(ResilienceOptions.DefaultPipelineName);
      // Both should be valid (non-null) pipelines.
      resolved.Should().NotBeNull();
      fallback.Should().NotBeNull();
   }

   [Fact]
   public void DeliberaResiliencePipelineProvider_AcceptsCustomPipeline()
   {
      var customPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
         .AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
         {
            Name = "MyCustom",
            MaxRetryAttempts = 7
         })
         .Build();

      var provider = new DeliberaResiliencePipelineProvider(
         Microsoft.Extensions.Options.Options.Create(new ResilienceOptions()).AsMonitor(),
         new[]
         {
            new KeyValuePair<string, Func<ResiliencePipelineBuilder<HttpResponseMessage>, ResiliencePipeline<HttpResponseMessage>>>(
               "Delibera.MyKey",
               _ => customPipeline)
         });

      var resolved = provider.GetPipeline("Delibera.MyKey");
      resolved.Should().BeSameAs(customPipeline);
   }

   [Fact]
   public void AddDeliberaResilience_RegistersPipelineProviderAndNamedHttpClients()
   {
      var services = new ServiceCollection();
      services.AddDelibera();
      services.AddDeliberaResilience(opts =>
      {
         opts.MaxRetryAttempts = 5;
         opts.BaseDelay = TimeSpan.FromMilliseconds(250);
      });

      using var sp = services.BuildServiceProvider();

      var registry = sp.GetRequiredService<IDeliberaResiliencePipelineProvider>();
      registry.GetPipeline(ResilienceOptions.LocalPipelineName).Should().NotBeNull();
      registry.GetPipeline(ResilienceOptions.CloudPipelineName).Should().NotBeNull();

      var factory = sp.GetRequiredService<IHttpClientFactory>();
      factory.CreateClient("Delibera.Ollama.Local").Should().NotBeNull();
      factory.CreateClient("Delibera.Ollama.Cloud").Should().NotBeNull();
      factory.CreateClient("Delibera.YandexGPT").Should().NotBeNull();
   }

   [Fact]
   public void AddDeliberaResilience_CustomPipeline_IsRegisteredAlongsideBuiltins()
   {
      var services = new ServiceCollection();
      services.AddDelibera();
      services.AddDeliberaResilience();

      services.AddDeliberaResiliencePipeline("Delibera.MyCustom", b => b
         .AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
         {
            Name = "Delibera.MyCustom",
            MaxRetryAttempts = 9
         })
         .Build());

      using var sp = services.BuildServiceProvider();

      var registry = sp.GetRequiredService<IDeliberaResiliencePipelineProvider>();
      var custom = registry.GetPipeline("Delibera.MyCustom");
      custom.Should().NotBeNull();
      ReferenceEquals(custom, ResiliencePipeline<HttpResponseMessage>.Empty).Should().BeFalse();
   }

   [Fact]
   public void AddDeliberaResilience_RespectsBindFromConfiguration()
   {
      var dict = new Dictionary<string, string?>
      {
         ["Delibera:Resilience:MaxRetryAttempts"] = "7",
         ["Delibera:Resilience:BaseDelay"] = "00:00:05",
         ["Delibera:Resilience:UseJitter"] = "true",
         ["Delibera:Resilience:RetryableStatusCodes:0"] = "429",
         ["Delibera:Resilience:RetryableStatusCodes:1"] = "500"
      };

      var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
         .AddInMemoryCollection(dict)
         .Build();

      var services = new ServiceCollection();
      services.AddDelibera(configuration);
      services.AddDeliberaResilience();

      using var sp = services.BuildServiceProvider();

      var monitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ResilienceOptions>>();
      var opts = monitor.CurrentValue;

      opts.MaxRetryAttempts.Should().Be(7);
      opts.BaseDelay.Should().Be(TimeSpan.FromSeconds(5));
      opts.UseJitter.Should().BeTrue();
      opts.RetryableStatusCodes.Should().Contain(new[] { 429, 500 });
   }
}

/// <summary>
///    Small helper to convert <see cref="Microsoft.Extensions.Options.IOptions{T}" /> into an
///    <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}" /> for tests where no
///    change-tracking is required.
/// </summary>
internal static class TestOptionsMonitorExtensions
{
   public static Microsoft.Extensions.Options.IOptionsMonitor<T> AsMonitor<T>(this Microsoft.Extensions.Options.IOptions<T> options)
      where T : class
   {
      return new StaticOptionsMonitor<T>(options.Value);
   }

   private sealed class StaticOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T> where T : class
   {
      public StaticOptionsMonitor(T value) => CurrentValue = value;
      public T CurrentValue { get; }
      public T Get(string? name) => CurrentValue;
      public IDisposable? OnChange(Action<T, string?> listener) => null;
   }
}