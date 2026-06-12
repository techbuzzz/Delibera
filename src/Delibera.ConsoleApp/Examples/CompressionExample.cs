using Delibera.Core.Compression;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
/// Demonstrates the 🔥 Killer Feature: Context Compression System.
/// Shows all 4 compression strategies, caching, token counting,
/// and full integration into the council debate flow.
/// </summary>
public static class CompressionExample
{
   /// <summary>
   /// Runs the context compression example.
   /// </summary>
   public static async Task RunAsync()
   {
      Console.WriteLine("═══════════════════════════════════════");
      Console.WriteLine("  🗜️  Context Compression Example");
      Console.WriteLine("═══════════════════════════════════════\n");

      // ── 1. Token Counter ──
      Console.WriteLine("📊 Token Counter Demo:\n");
      var counter = TokenCounter.Default;

      var sampleTexts = new[]
      {
            "Hello, world!",
            "The quick brown fox jumps over the lazy dog.",
            "In the field of artificial intelligence, large language models (LLMs) have revolutionized natural language processing by demonstrating remarkable capabilities in text generation, summarization, translation, and reasoning tasks.",
            new string('x', 1000) // 1000 chars of 'x'
        };

      foreach (var text in sampleTexts)
      {
         var tokens = counter.EstimateTokens(text);
         Console.WriteLine($"  \"{text[..Math.Min(60, text.Length)]}...\" → ~{tokens} tokens ({text.Length} chars)");
      }

      // ── 2. Deduplication Compressor (no embeddings needed) ──
      Console.WriteLine("\n\n🧹 Deduplication Compressor (heuristic mode):\n");

      var duplicatedText = """
            Microservices architecture provides independent deployment of services.
            Each service can be deployed independently without affecting others.
            Services are independently deployable and scalable.
            The architecture supports polyglot programming with different languages per service.
            Different programming languages can be used for each microservice.
            Fault isolation ensures that failures in one service don't cascade to others.
            When one service fails, it doesn't bring down the entire system.
            Monitoring and debugging distributed systems is complex.
            Debugging across service boundaries adds significant complexity.
            """;

      var dedupCompressor = new DeduplicationCompressor();
      var dedupResult = await dedupCompressor.CompressAsync(duplicatedText);

      PrintCompressionResult("Deduplication", dedupResult);

      // ── 3. Compression Cache ──
      Console.WriteLine("\n💾 Compression Cache Demo:\n");

      var cache = new CompressionCache(maxEntries: 100);

      // First call — cache miss
      cache.TryGet(duplicatedText, "Deduplication", out _);
      Console.WriteLine($"  After miss: {cache.GetSummary()}");

      // Store result
      cache.Set(duplicatedText, "Deduplication", dedupResult);
      Console.WriteLine($"  After set:  {cache.GetSummary()}");

      // Second call — cache hit
      var hit = cache.TryGet(duplicatedText, "Deduplication", out var cachedResult);
      Console.WriteLine($"  After hit:  {cache.GetSummary()}");
      Console.WriteLine($"  Cache hit: {hit}, result matches: {cachedResult?.Text == dedupResult.Text}");

      // ── 4. Compression Factory ──
      Console.WriteLine("\n\n🏭 Compression Factory Demo:\n");

      var none = CompressionFactory.Create(CompressionStrategy.None);
      Console.WriteLine($"  None:           {none.StrategyName} — {none.Description}");

      var dedup = CompressionFactory.Create(CompressionStrategy.Deduplication);
      Console.WriteLine($"  Deduplication:   {dedup.StrategyName} — {dedup.Description}");

      Console.WriteLine("\n  (Semantic, Summarization, and Hybrid strategies require");
      Console.WriteLine("   IEmbeddingProvider / ILLMProvider — see full demo below)");

      // ── 5. Compression Options ──
      Console.WriteLine("\n\n⚙️  Compression Options:\n");

      var options = new CompressionOptions
      {
         TargetRatio = 0.3,
         PreserveCodeBlocks = true,
         PreserveStructuredContent = true,
         DeduplicationThreshold = 0.8,
         SummarizationTemperature = 0.2f
      };

      Console.WriteLine($"  Target ratio:       {options.TargetRatio:P0}");
      Console.WriteLine($"  Preserve code:      {options.PreserveCodeBlocks}");
      Console.WriteLine($"  Preserve structure:  {options.PreserveStructuredContent}");
      Console.WriteLine($"  Dedup threshold:     {options.DeduplicationThreshold:F2}");
      Console.WriteLine($"  Summ. temperature:   {options.SummarizationTemperature:F1}");

      var aggressiveResult = await dedupCompressor.CompressAsync(duplicatedText, options);
      PrintCompressionResult("Dedup (aggressive)", aggressiveResult);

      // ── 6. Council Integration ──
      Console.WriteLine("\n\n🏛️  Council Builder with Compression:\n");

      Console.WriteLine("  // Example: how to wire compression into the council");
      Console.WriteLine("  var executor = new CouncilBuilder()");
      Console.WriteLine("      .AddMember(\"llama2\", provider)");
      Console.WriteLine("      .SetChairman(\"qwen2.5\", provider)");
      Console.WriteLine("      .WithCompression(CompressionStrategy.Deduplication)");
      Console.WriteLine("      .WithCompressionOptions(new CompressionOptions { TargetRatio = 0.5 })");
      Console.WriteLine("      .WithCompressionCache()");
      Console.WriteLine("      .WithUserPrompt(\"...\")");
      Console.WriteLine("      .Build();");

      Console.WriteLine("\n  // Or with a custom compressor:");
      Console.WriteLine("  var compressor = CompressionFactory.Create(");
      Console.WriteLine("      CompressionStrategy.Hybrid,");
      Console.WriteLine("      llmProvider: provider,");
      Console.WriteLine("      modelName: \"llama2\",");
      Console.WriteLine("      embeddingProvider: embeddings);");
      Console.WriteLine("  builder.WithCompression(compressor);");

      // ── 7. Token Statistics ──
      Console.WriteLine("\n\n📊 Token Statistics (example output):\n");

      var stats = new TokenStatistics
      {
         TotalOriginalTokens = 12500,
         TotalCompressedTokens = 7800,
         TotalResponseTokens = 6200,
         RoundBreakdown =
          [
              new RoundTokenUsage(1, "Initial Responses", 3200, 3200, 2100, "None"),
                new RoundTokenUsage(2, "Critique", 5100, 2800, 2400, "Hybrid"),
                new RoundTokenUsage(3, "Improved Responses", 4200, 1800, 1700, "Hybrid")
          ]
      };

      Console.WriteLine(stats.ToSummary());

      Console.WriteLine("═══ Compression Example Complete ═══\n");
   }

   private static void PrintCompressionResult(string label, CompressedContext result)
   {
      Console.WriteLine($"\n  {label}:");
      Console.WriteLine($"    Original:    {result.OriginalTokens} tokens ({result.OriginalLength} chars)");
      Console.WriteLine($"    Compressed:  {result.CompressedTokens} tokens ({result.CompressedLength} chars)");
      Console.WriteLine($"    Ratio:       {result.CompressionRatio:P1}");
      Console.WriteLine($"    Saved:       {result.TokensSavedPercent:F1}%");
      Console.WriteLine($"    Duration:    {result.Duration.TotalMilliseconds:F1}ms");
      Console.WriteLine($"    Strategy:    {result.StrategyUsed}");
      var preview = result.Text.Length > 200 ? result.Text[..200] + "…" : result.Text;
      Console.WriteLine($"    Preview:     {preview.Replace("\n", " ").Replace("\r", "")}");
   }
}
