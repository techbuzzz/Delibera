using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Persistence;
using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.Configuration;

namespace Delibera.Cli;

/// <summary>
///    Everything the CLI needs from the framework, isolated from argument parsing so the
///    behaviour is testable without a process.
/// </summary>
public static class DebateRunner
{
   /// <summary>What to debate and how.</summary>
   /// <param name="Question">The question.</param>
   /// <param name="Strategy">Strategy name.</param>
   /// <param name="MaxRounds">Rounds to run.</param>
   /// <param name="Temperature">Sampling temperature.</param>
   public sealed record Request
   {
      /// <summary>The question put to the council.</summary>
      public required string Question { get; init; }

      /// <summary>Debate strategy name.</summary>
      public string Strategy { get; init; } = "standard";

      /// <summary>Rounds to run.</summary>
      public int MaxRounds { get; init; } = 3;

      /// <summary>Sampling temperature.</summary>
      public float Temperature { get; init; } = 0.7f;
   }

   /// <summary>
   ///   Builds a council from the ambient provider configuration.
   /// </summary>
   /// <remarks>
   ///   Members come from the <c>Delibera:Providers</c> configuration section so the CLI honours
   ///   the same settings as the server. With nothing configured the command fails with an
   ///   explanation rather than silently producing an empty debate.
   /// </remarks>
   /// <param name="request">What to debate.</param>
   /// <param name="configuration">Ambient configuration.</param>
   public static ICouncilBuilder BuildCouncil(Request request, IConfiguration configuration)
   {
      ArgumentNullException.ThrowIfNull(request);
      ArgumentNullException.ThrowIfNull(configuration);

      ICouncilBuilder builder = new CouncilBuilder()
         .WithSystemPrompt("You are an expert taking part in a structured debate. Argue your position clearly.")
         .WithUserPrompt(request.Question)
         .WithTemperature(request.Temperature)
         .WithMaxRounds(request.MaxRounds)
         .WithStandardDebate();

      var added = 0;
      foreach (var (modelKey, created) in ReadProviders(configuration))
      {
         builder = builder.AddMember(modelKey, created, modelKey);
         added++;
      }

      if (added == 0)
      {
         throw new InvalidOperationException(
            "No providers configured. The CLI reads Delibera:Providers:<provider>:Models from "
            + "appsettings.json, or from environment variables shaped like "
            + "DELIBERA__PROVIDERS__OLLAMA__MODELS__LLAMA3__ENDPOINT.");
      }

      return builder;
   }

   /// <summary>
   ///   Reads configured models and instantiates a provider for each.
   /// </summary>
   /// <remarks>
   ///   Deliberately limited to the two providers that can be constructed from configuration
   ///   alone. Anything richer — an Azure OpenAI client, an MCP-backed operator — belongs in
   ///   host code, not in a command-line tool that has no composition root.
   /// </remarks>
   private static IEnumerable<(string Model, ILLMProvider Provider)> ReadProviders(IConfiguration configuration)
   {
      var providers = configuration.GetSection("Delibera:Providers").GetChildren();

      foreach (var provider in providers)
      {
         foreach (var model in provider.GetSection("Models").GetChildren())
         {
            var endpoint = model["Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint)) continue;

            var name = model["Deployment"] ?? model.Key;

            ILLMProvider created = provider.Key.ToLowerInvariant() switch
            {
               "ollama" => new OllamaProvider(endpoint),
               "yandex" => new YandexGptProvider(
                  endpoint,
                  model["ApiKey"] ?? configuration["Delibera:Yandex:ApiKey"] ?? "",
                  name),
               _ => throw new NotSupportedException(
                  $"Provider '{provider.Key}' is not supported by the CLI. Use 'ollama' or 'yandex'.")
            };

            yield return (name, created);
         }
      }
   }

   /// <summary>Runs a debate, optionally printing each round as it lands.</summary>
   /// <param name="request">What to debate.</param>
   /// <param name="stream">Print rounds as they complete.</param>
   /// <param name="verbose">Print execution logs.</param>
   /// <param name="ct">Cancellation token.</param>
   public static async Task<DebateResult> RunAsync(
      Request request,
      bool stream,
      bool verbose,
      CancellationToken ct = default)
   {
      var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
      var executor = BuildCouncil(request, configuration).Build();

      if (verbose)
      {
         executor.OnLog += entry =>
            Console.Error.WriteLine($"[{entry.Level}] {entry.Source}: {entry.Message}");
      }

      if (!stream)
         return await executor.ExecuteAsync(ct).ConfigureAwait(false);

      await using var enumerator = executor.StreamDebateAsync(ct).GetAsyncEnumerator(ct);
      while (await enumerator.MoveNextAsync().ConfigureAwait(false))
      {
         var round = enumerator.Current;
         Console.Error.WriteLine($"── Round {round.RoundNumber}: {round.RoundName} ──");
         foreach (var (member, response) in round.Responses)
         {
            var cut = response.IndexOf('\n');
            var preview = cut < 0 ? response : response[..cut];
            Console.Error.WriteLine($"  {member}: {(preview.Length > 160 ? preview[..160] + "…" : preview)}");
         }
      }

      // The aggregated result is published on the executor once the stream completes; taking it
      // from there rather than re-running keeps --stream and --json from costing two debates.
      return executor.LastStreamedResult
         ?? throw new InvalidOperationException("The debate stream ended without producing a result.");
   }

   /// <summary>Loads a debate from a checkpoint store.</summary>
   /// <param name="debateId">Debate identifier.</param>
   /// <param name="storePath">Checkpoint directory.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>
   ///   A <see cref="DebateResult" /> reconstructed from the rounds the checkpoint recorded, or
   ///   <c>null</c> when no checkpoint exists.
   /// </returns>
   /// <remarks>
   ///   A checkpoint stores the rounds that completed plus the options they ran under, not a
   ///   finished <see cref="DebateResult" />. This reports that progress rather than continuing
   ///   the debate: continuing would mean rehydrating the whole provider set from the stored
   ///   options snapshot, which is the host's job, not the CLI's. The reconstruction is marked
   ///   <c>IsCompleted = false</c> so it cannot be mistaken for a verdict.
   /// </remarks>
   public static async Task<DebateResult?> ResumeAsync(
      string debateId,
      string storePath,
      CancellationToken ct = default)
   {
      using var store = new FileDebateStore(storePath);
      var checkpoint = await store.LoadCheckpointAsync(debateId, ct).ConfigureAwait(false);
      if (checkpoint is null) return null;

      var participants = checkpoint.CompletedRounds
         .SelectMany(r => r.Responses.Keys)
         .Distinct(StringComparer.Ordinal)
         .ToList();

      return new DebateResult
      {
         DebateId = checkpoint.DebateId,
         StrategyName = checkpoint.Options.Strategy ?? "standard",
         Context = new PromptContext(UserPrompt: checkpoint.OriginalQuestion),
         Participants = participants,
         Rounds = checkpoint.CompletedRounds,
         StartedAt = checkpoint.CreatedAt.UtcDateTime
      };
   }

   /// <summary>Reads a serialized <see cref="DebateResult" /> from disk.</summary>
   /// <param name="path">File path.</param>
   /// <returns>The result, or <c>null</c> when the file is not a serialized result.</returns>
   public static DebateResult? LoadResult(string path)
   {
      try
      {
         return JsonSerializer.Deserialize<DebateResult>(File.ReadAllText(path), JsonOptions);
      }
      catch (Exception ex) when (ex is JsonException or IOException)
      {
         return null;
      }
   }

   /// <summary>Serializes a result for <c>--json</c> and for <c>compare</c> to read back.</summary>
   /// <param name="result">The result to serialize.</param>
   public static string ToJson(DebateResult result)
      => JsonSerializer.Serialize(result, JsonOptions);

   /// <summary>
   ///   Runs the same debate repeatedly and reports the distribution.
   /// </summary>
   /// <remarks>
   ///   Wall time is dominated by how much the models choose to write, so a single run tells you
   ///   nothing; the spread across runs is the number worth reading.
   /// </remarks>
   /// <param name="question">The question to benchmark.</param>
   /// <param name="iterations">How many runs to perform.</param>
   /// <param name="rounds">Rounds per run.</param>
   /// <param name="ct">Cancellation token.</param>
   public static async Task<BenchmarkReport> BenchmarkAsync(
      string question,
      int iterations,
      int rounds,
      CancellationToken ct = default)
   {
      var results = new List<DebateResult>(iterations);

      for (var i = 0; i < iterations; i++)
      {
         Console.Error.WriteLine($"── iteration {i + 1}/{iterations} ──");
         results.Add(await RunAsync(
            new Request { Question = question, MaxRounds = rounds },
            stream: false, verbose: false, ct).ConfigureAwait(false));
      }

      return BenchmarkReport.From(question, results);
   }

   /// <summary>
   ///   Serializer settings shared by <c>--json</c> and <c>compare</c>.
   /// </summary>
   /// <remarks>
   ///   Reflection-based resolution rather than source generation: this is a one-shot CLI that
   ///   serializes a handful of objects, so a generated context would cost more than it saves.
   /// </remarks>
   private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
   {
      WriteIndented = false,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
   };
}

/// <summary>
///    Timing summary across repeated runs of the same debate.
/// </summary>
public sealed record BenchmarkReport
{
   /// <summary>The benchmarked question.</summary>
   public required string Question { get; init; }

   /// <summary>How many runs completed.</summary>
   public required int Runs { get; init; }

   /// <summary>Fastest run.</summary>
   public required TimeSpan Fastest { get; init; }

   /// <summary>Slowest run.</summary>
   public required TimeSpan Slowest { get; init; }

   /// <summary>Arithmetic mean.</summary>
   public required TimeSpan Mean { get; init; }

   /// <summary>Mean cost, when a price list was configured.</summary>
   public decimal? MeanCost { get; init; }

   /// <summary>How many runs had at least one member fail.</summary>
   public required int DegradedRuns { get; init; }

   /// <summary>Builds a report from the completed runs.</summary>
   /// <param name="question">The benchmarked question.</param>
   /// <param name="results">Completed results.</param>
   /// <exception cref="ArgumentException">No runs were supplied.</exception>
   public static BenchmarkReport From(string question, IReadOnlyList<DebateResult> results)
   {
      ArgumentNullException.ThrowIfNull(results);
      if (results.Count == 0)
         throw new ArgumentException("At least one run is required.", nameof(results));

      var durations = results.Select(r => r.TotalDuration).OrderBy(d => d).ToList();
      var ticks = durations.Aggregate(TimeSpan.Zero, (a, b) => a + b);

      return new BenchmarkReport
      {
         Question = question,
         Runs = results.Count,
         Fastest = durations[0],
         Slowest = durations[^1],
         Mean = TimeSpan.FromTicks(ticks.Ticks / results.Count),
         MeanCost = results.Any(r => r.CostEstimate is not null)
            ? results.Where(r => r.CostEstimate is not null).Average(r => r.CostEstimate!.TotalCost)
            : null,
         DegradedRuns = results.Count(r => r.IsDegraded)
      };
   }

   /// <summary>Renders the report as Markdown.</summary>
   public string ToMarkdown()
   {
      var sb = new StringBuilder();
      sb.AppendLine("# Benchmark");
      sb.AppendLine();
      sb.AppendLine($"**Question:** {Question}");
      sb.AppendLine($"**Runs:** {Runs}");
      sb.AppendLine();
      sb.AppendLine("| Metric | Value |");
      sb.AppendLine("|---|---:|");
      sb.AppendLine($"| Fastest | {Fastest:mm\\:ss\\.fff} |");
      sb.AppendLine($"| Mean | {Mean:mm\\:ss\\.fff} |");
      sb.AppendLine($"| Slowest | {Slowest:mm\\:ss\\.fff} |");
      if (MeanCost is { } cost)
         sb.AppendLine($"| Mean cost | {cost:F4} |");
      sb.AppendLine($"| Degraded runs | {DegradedRuns} |");
      sb.AppendLine();
      return sb.ToString();
   }
}