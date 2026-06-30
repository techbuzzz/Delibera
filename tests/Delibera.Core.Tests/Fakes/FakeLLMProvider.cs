using Delibera.Core.Interfaces;

namespace Delibera.Core.Tests.Fakes;

/// <summary>
///    In-memory <see cref="ILLMProvider" /> used for tests that need to drive a
///    council end-to-end without network access. Optionally delays each call so
///    cancellation can be exercised in-flight.
/// </summary>
public sealed class FakeLLMProvider(
   string providerName = "Fake",
   string reply = "fake-reply",
   int chatDelayMs = 0) : ILLMProvider
{
   public int ChatCallCount { get; private set; }

   public string ProviderName => providerName;

   public Task<bool> IsAvailableAsync(CancellationToken ct = default)
      => Task.FromResult(true);

   public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<string>>(new[] { "fake-model" });

   public async Task<string> ChatAsync(
      string model,
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      CancellationToken ct = default)
   {
      ChatCallCount++;
      if (chatDelayMs > 0)
         await Task.Delay(chatDelayMs, ct);
      return reply;
   }

   public void Dispose() { /* no-op */ }
}
