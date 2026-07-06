using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Telemetry;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the F-08 OpenTelemetry-style observability layer.
///    A private <see cref="ActivityListener" /> and <see cref="MeterListener" /> are
///    installed in-process so the demo prints every span and metric that Delibera emits
///    during a debate — without requiring a real Jaeger/Prometheus backend.
/// </summary>
/// <remarks>
///    In production you would instead wire the standard <c>OpenTelemetry.*</c> builder
///    APIs (e.g. <c>WithTracing(b =&gt; b.AddSource("Delibera.Council").AddJaegerExporter())</c>)
///    and let the SDK export to your observability backend. This demo just proves the
///    instrumentation is wired correctly.
/// </remarks>
public static class TelemetryExample
{
   /// <summary>Runs the telemetry example against any reachable Ollama endpoint.</summary>
   public static async Task RunAsync(CancellationToken ct = default)
   {
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  📊 Telemetry Example (F-08)");
      Console.WriteLine("═══════════════════════════════════════════");
      Console.WriteLine("  Installs in-process ActivityListener + MeterListener");
      Console.WriteLine("  and prints every span / metric emitted by Delibera.");
      Console.WriteLine();

      // ── Install in-process listeners so we can observe what Delibera emits ──
      var spans = new ConcurrentBag<(string Op, ActivityStatusCode Status, long Ms)>();
      var metrics = new ConcurrentBag<(string Instrument, double Value)>();

      using var activityListener = new ActivityListener
      {
         ShouldListenTo = src => src.Name == DeliberaActivitySource.DefaultName,
         SampleUsingParentId = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
         Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
         ActivityStopped = activity =>
         {
            var ms = activity.Duration.TotalMilliseconds;
            spans.Add((activity.OperationName, activity.Status, (long)ms));
         }
      };
      ActivitySource.AddActivityListener(activityListener);

      using var meterListener = new MeterListener();
      meterListener.InstrumentPublished = (instrument, listener) =>
      {
         if (instrument.Meter.Name == DeliberaMeter.DefaultName)
            listener.EnableMeasurementEvents(instrument);
      };
      meterListener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
         metrics.Add((instrument.Name, measurement)));
      meterListener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
         metrics.Add((instrument.Name, measurement)));
      meterListener.Start();

      // ── Provider setup ──
      using var factory = new ProviderFactory();
      OllamaProvider? ollama = null;
      try
      {
         ollama = factory.CreateLocalOllama("http://localhost:11434");
         if (!await ollama.IsAvailableAsync(ct))
         {
            Console.WriteLine("  ⚠️  Local Ollama not available — falling back to Ollama Cloud.");
            ollama.Dispose();
            ollama = null;
         }
      }
      catch (Exception ex)
      {
         Console.WriteLine($"  ⚠️  Could not initialise local Ollama: {ex.Message}");
      }

      if (ollama is null)
      {
         var apiKey = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
         if (string.IsNullOrWhiteSpace(apiKey))
         {
            Console.WriteLine("  ❌ No Ollama provider available (no local server, no OLLAMA_API_KEY env).");
            Console.WriteLine("     Skipping live demo. The telemetry plumbing is wired regardless —");
            Console.WriteLine("     run against any reachable Ollama to see spans & metrics printed.");
            return;
         }

         ollama = factory.CreateOllama("https://api.ollama.com", apiKey);
      }

      ILLMProvider llm = ollama;

      // ── Build a small council with telemetry enabled ──
      var executor = new CouncilBuilder()
         .AddMember("llama3.2", llm, "Analyst")
         .AddMember("qwen2.5", llm, "Critic")
         .SetChairman(Chairman.CreateStandard("qwen2.5", llm))
         .WithStandardDebate()
         .WithSystemPrompt("You are a thoughtful software architect.")
         .WithUserPrompt("Should a 5-person startup adopt microservices or a modular monolith?")
         .WithMaxRounds(1)
         .WithTemperature(0.4f)
         .WithTelemetry()
         .SaveResultTo("./debate_results/telemetry_demo.md")
         .Build();

      Console.WriteLine($"  Telemetry enabled: {executor.IsTelemetryEnabled}");
      Console.WriteLine($"  ActivitySource:    {DeliberaActivitySource.DefaultName}");
      Console.WriteLine($"  Meter:             {DeliberaMeter.DefaultName}");
      Console.WriteLine();
      Console.WriteLine("  Starting debate…");
      Console.WriteLine();

      executor.OnRoundCompleted += round =>
         Console.WriteLine($"  ✅ Round {round.RoundNumber} ({round.RoundName}) in {round.Duration.TotalMilliseconds:F0}ms");

      var sw = Stopwatch.StartNew();
      var result = await executor.ExecuteAsync(ct);
      sw.Stop();

      // Allow listeners a brief moment to flush callbacks.
      await Task.Delay(50, CancellationToken.None);

      Console.WriteLine();
      Console.WriteLine($"  🏆 Debate completed in {sw.Elapsed.TotalMilliseconds:F0}ms");
      Console.WriteLine($"     Rounds:        {result.Rounds.Count}");
      Console.WriteLine($"     Verdict chars: {result.FinalVerdict?.Length ?? 0}");
      Console.WriteLine();

      // ── Print collected spans ──
      Console.WriteLine("  ── Emitted spans ──");
      foreach (var (op, status, ms) in spans.OrderBy(s => s.Op))
      {
         var statusIcon = status switch
         {
            ActivityStatusCode.Ok => "✅",
            ActivityStatusCode.Error => "❌",
            _ => "•"
         };
         Console.WriteLine($"    {statusIcon} {op,-45} {ms,5}ms");
      }

      Console.WriteLine();
      Console.WriteLine("  ── Emitted metrics ──");
      foreach (var (instrument, value) in metrics.OrderBy(m => m.Instrument))
         Console.WriteLine($"    • {instrument,-40} {value,12:F2}");

      Console.WriteLine();
      Console.WriteLine("  💡 In production, wire these into OpenTelemetry:");
      Console.WriteLine("     services.AddOpenTelemetry()");
      Console.WriteLine("         .WithTracing(b => b.AddSource(\"Delibera.Council\").AddJaegerExporter())");
      Console.WriteLine("         .WithMetrics(b => b.AddMeter(\"Delibera.Metrics\").AddPrometheusExporter())");
   }
}
