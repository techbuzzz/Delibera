using Delibera.Core.Compression;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Context compression was fully implemented - four compressors, a cache, options, telemetry and
///    HTML reporting - but never called from the debate pipeline. <c>CompressTextAsync</c> was
///    public API that nothing invoked, <c>DebateResult.TokenStats</c> was never assigned, and ten
///    measured runs with compression enabled reported 0.00% saved at 0 ms overhead while context
///    grew past 10,000 tokens per round.
///    <para>
///    These tests pin the wiring: a configured compressor must now actually run on round prompts,
///    and its savings must be observable on the result.
///    </para>
/// </summary>
public sealed class ContextCompressionWiringTests
{
   /// <summary>
   ///    Deterministic compressor that keeps roughly half the text. Real compressors depend on
   ///    embeddings or an LLM; this one isolates the wiring under test from model behaviour.
   /// </summary>
   private sealed class HalvingCompressor : IContextCompressor
   {
      public int Calls;

      public string StrategyName => "Halving";

      public string Description => "Test compressor that keeps the first half of the text.";

      public Task<CompressedContext> CompressAsync(
         string text, CompressionOptions? options = null, CancellationToken ct = default)
      {
         Interlocked.Increment(ref Calls);
         var counter = TokenCounter.Default;
         var original = counter.EstimateTokens(text);

         // Leave a marker so a test can tell a compressed prompt from an untouched one.
         var kept = text[..(text.Length / 2)] + "\n[COMPRESSED]";

         return Task.FromResult(new CompressedContext
         {
            Text = kept,
            OriginalLength = text.Length,
            CompressedLength = kept.Length,
            OriginalTokens = original,
            CompressedTokens = counter.EstimateTokens(kept),
            StrategyUsed = StrategyName
         });
      }

      public Task<CompressedContext> CompressBatchAsync(
         IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default)
      {
         var merged = string.Join("\n\n", texts);
         return CompressAsync(merged, options, ct);
      }
   }

   private const string Stub = "You are a helpful assistant participating in a council debate.";

   private static Task<string> ReplyAsync(
      string model, string systemPrompt, string userPrompt, float temperature = 0.7f, CancellationToken ct = default) =>
      Task.FromResult($"concise reply from {model}");

   private static async Task<DebateResult> RunDebateAsync(IContextCompressor? compressor, int rounds = 2)
   {
      // Comfortably above the compression threshold, so the wiring is exercised rather than
      // short-circuited by the "prompt too small to bother" guard.
      var padding = string.Join(' ', Enumerable.Repeat(
            "The gateway terminates TLS, enforces rate limits, records audit entries and fans out to services.", 120));

      var provider = new StubProvider();
      ICouncilBuilder builder = new CouncilBuilder()
         .WithUserPrompt($"We need a decision about the gateway. {padding}")
         .WithMaxRounds(rounds)
         .WithStandardDebate()
         .AddMember("a", provider)
         .AddMember("b", provider)
         .SetChairman("chair", provider);

      if (compressor is not null)
         builder = builder.WithCompression(compressor);

      var executor = builder.Build();
      var exec = executor as CouncilExecutor;
      if (exec is not null && compressor is not null)
         Assert.True(exec.ExecutionOptions.HasCompressor,
            $"compression never reached the execution options (Compressor={exec.Compressor?.StrategyName ?? "null"})");

      var promptTokens = TokenCounter.Default.EstimateTokens(
         $"We need a decision about the gateway. {string.Join(' ', Enumerable.Repeat(
            "The gateway terminates TLS, enforces rate limits, records audit entries and fans out to services.", 120))}");
      Assert.True(promptTokens > 1200, $"test padding is only {promptTokens} tokens");

      return await executor.ExecuteAsync().ConfigureAwait(false);
   }

   private sealed class StubProvider : ILLMProvider
   {
      public string ProviderName => "Stub";

      public Task<string> ChatAsync(
         string model, string systemPrompt, string userPrompt,
         float temperature = 0.7f, CancellationToken ct = default) => ReplyAsync(model, systemPrompt, userPrompt, temperature, ct);

      public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

      public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
         Task.FromResult<IReadOnlyList<string>>(["a", "b", "chair"]);

      public Task<ModelCapabilities> GetModelCapabilitiesAsync(string model, CancellationToken ct = default) =>
         Task.FromResult(ModelCapabilities.Unknown(model));

      public void Dispose()
      {
      }
   }

   [Fact]
   public async Task Configured_Compressor_Is_Actually_Invoked_By_A_Debate()
   {
      var compressor = new HalvingCompressor();

      await RunDebateAsync(compressor);

      compressor.Calls.Should().BeGreaterThan(0,
         "a configured compressor must be called by the debate, not only reachable from CompressTextAsync");
   }

   [Fact]
   public async Task Debate_Result_Reports_What_Compression_Saved()
   {
      var result = await RunDebateAsync(new HalvingCompressor());

      result.CompressionLogs.Should().NotBeEmpty("each compression pass must be recorded");
      result.TokenStats.Should().NotBeNull("TokenStats was previously never assigned anywhere in the library");

      result.TokenStats!.SavedPercent.Should().BeGreaterThan(0);
      result.TokenStats.TokensSaved.Should().BeGreaterThan(0);
   }

   [Fact]
   public async Task No_Compressor_Configured_Leaves_Token_Stats_Unset_And_Makes_No_Calls()
   {
      var result = await RunDebateAsync(null);

      result.CompressionLogs.Should().BeEmpty();
      result.TokenStats.Should().BeNull("no compressor means nothing to report, and nothing should be invented");
   }

   [Fact]
   public async Task A_Compressor_That_Returns_More_Text_Is_Not_Trusted()
   {
      var result = await RunDebateAsync(new InflatingCompressor());

      result.CompressionLogs.Should().NotBeEmpty("the attempt is still recorded");
      result.TokenStats!.SavedPercent.Should().Be(0,
         "a compressor that grows the prompt must not replace it");
   }

   private sealed class InflatingCompressor : IContextCompressor
   {
      public string StrategyName => "Inflating";

      public string Description => "Test compressor that makes the prompt bigger.";

      public Task<CompressedContext> CompressAsync(
         string text, CompressionOptions? options = null, CancellationToken ct = default)
      {
         var counter = TokenCounter.Default;
         var bigger = text + new string('x', text.Length);
         return Task.FromResult(new CompressedContext
         {
            Text = bigger,
            OriginalLength = text.Length,
            CompressedLength = bigger.Length,
            OriginalTokens = counter.EstimateTokens(text),
            CompressedTokens = counter.EstimateTokens(bigger),
            StrategyUsed = StrategyName
         });
      }

      public Task<CompressedContext> CompressBatchAsync(
         IReadOnlyList<string> texts, CompressionOptions? options = null, CancellationToken ct = default) =>
         CompressAsync(string.Join("\n\n", texts), options, ct);
   }
}