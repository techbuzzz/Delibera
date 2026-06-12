using Delibera.Core.DependencyInjection;
using Delibera.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
/// Demonstrates Delibera usage with Dependency Injection.
/// Shows how to register services, resolve interfaces, and build a council via DI.
/// </summary>
public static class DIExample
{
   /// <summary>
   /// Runs the DI example — builds a service provider, resolves council services,
   /// and demonstrates the full DI workflow.
   /// </summary>
   public static async Task RunAsync()
   {
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  🏛️  Delibera — DI Example (v3.1)");
      Console.WriteLine("═══════════════════════════════════════════\n");

      // ── 1. Load configuration ──
      var configuration = new ConfigurationBuilder()
          .SetBasePath(Directory.GetCurrentDirectory())
          .AddJsonFile("appsettings.json", optional: false)
          .Build();

      // ── 2. Build service collection with Delibera ──
      var services = new ServiceCollection();

      // Option A: Register with configuration binding
      services.AddDelibera(configuration, "Delibera");

      // Option B: Register with action delegate (alternative)
      // services.AddDelibera(options =>
      // {
      //     options.Strategy = "Standard";
      //     options.MaxRounds = 4;
      //     options.Temperature = 0.7f;
      //     options.Compression.Enabled = true;
      //     options.Compression.Strategy = "Hybrid";
      // });

      var sp = services.BuildServiceProvider();

      // ── 3. Resolve services from DI ──
      var providerFactory = sp.GetRequiredService<ILLMProviderFactory>();
      var compressionFactory = sp.GetRequiredService<ICompressionFactory>();
      var councilBuilder = sp.GetRequiredService<ICouncilBuilder>();
      var options = sp.GetService<IOptions<CouncilOptions>>();
      var opts = options?.Value;

      Console.WriteLine("✅ Services resolved from DI container:");
      Console.WriteLine($"   ILLMProviderFactory: {providerFactory.GetType().Name}");
      Console.WriteLine($"   ICompressionFactory: {compressionFactory.GetType().Name}");
      Console.WriteLine($"   ICouncilBuilder:     {councilBuilder.GetType().Name}");

      if (opts is not null)
      {
         Console.WriteLine($"\n📋 CouncilOptions from configuration:");
         Console.WriteLine($"   Strategy:     {opts.Strategy}");
         Console.WriteLine($"   MaxRounds:    {opts.MaxRounds}");
         Console.WriteLine($"   Temperature:  {opts.Temperature:F2}");
         Console.WriteLine($"   Compression:  {(opts.Compression.Enabled ? "Enabled" : "Disabled")} ({opts.Compression.Strategy})");
         Console.WriteLine($"   Output:       {opts.Output.Directory} (Separate: {opts.Output.SeparateFiles})");
      }

      // ── 4. Create providers via factory ──
      Console.WriteLine("\n🔧 Creating providers...");
      var providerConfig = configuration.GetSection("DeliberaApp:Providers:OllamaCloud");
      var provider = providerFactory.Create("OllamaCloud", "Ollama", providerConfig);
      Console.WriteLine($"   Created provider: {provider.ProviderName}");

      // ── 5. Build and configure council via interface ──
      Console.WriteLine("\n🏛️  Building council via ICouncilBuilder...");
      var executor = councilBuilder
          .AddMember("llama2", provider, "Expert", "Analytical expert")
          .AddMember("qwen2.5", provider, "Expert", "Creative thinker")
          .SetChairman("qwen2.5", provider)
          .WithSystemPrompt(opts?.SystemPrompt ?? "You are a council debate participant.")
          .WithUserPrompt("What is the best approach to software architecture?")
          .WithMaxRounds(opts?.MaxRounds ?? 4)
          .WithTemperature(opts?.Temperature ?? 0.7f)
          .Build();

      Console.WriteLine(executor.GetInfo());

      Console.WriteLine("✅ Council built successfully via DI!");
      Console.WriteLine("\n💡 Note: To actually run the debate, call executor.ExecuteAsync()");
      Console.WriteLine("   This requires a running Ollama instance or valid API key.\n");

      // Cleanup
      providerFactory.Dispose();
      await Task.CompletedTask;
   }
}
