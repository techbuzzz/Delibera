using Delibera.Core.Interfaces;
using Delibera.Core.Models;

namespace Delibera.Core.Tests.Fakes;

/// <summary>
///    In-memory <see cref="ILLMProvider" /> used for tests that need to drive a
///    council end-to-end without network access.
/// </summary>
/// <remarks>
///    <para>
///    <b>Every call suspends by default.</b> That is deliberate and load-bearing.
///    </para>
///    <para>
///    A provider that returns <c>Task.FromResult</c> completes synchronously, so the whole
///    debate runs inline. Code that is only correct while nothing suspends — a callback
///    installed on a field and removed in a <c>finally</c>, for instance — then looks fine.
///    This fake did exactly that for the streaming path: eleven tests were green while
///    <c>StreamDebateAsync</c> returned no rounds at all against any real provider, because a
///    real provider awaits network I/O and the callback had already been uninstalled.
///    </para>
///    <para>
///    Rule of thumb for this suite: <i>any test of a callback, event or continuation must use
///    a provider that suspends at least once before completing</i>. The default now does, so
///    the only way to opt out is to ask for it explicitly with
///    <c>suspends: false</c> — and doing so should be justified in the test.
///    </para>
/// </remarks>
public sealed class FakeLLMProvider(
   string providerName = "Fake",
   string reply = "fake-reply",
   int chatDelayMs = 0,
   bool suspends = true) : ILLMProvider
{
   public int ChatCallCount { get; private set; }

   public string ProviderName => providerName;

   /// <summary>
   ///    Whether this provider actually suspends before completing. A test can assert on it
   ///    to make the guarantee explicit instead of implicit.
   /// </summary>
   public bool Suspends { get; } = suspends;

   public Task<bool> IsAvailableAsync(CancellationToken ct = default)
      => Task.FromResult(true);

   public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<string>>(new[] { "fake-model" });

   public Task<ModelCapabilities> GetModelCapabilitiesAsync(string model, CancellationToken ct = default)
      => Task.FromResult(ModelCapabilities.Unknown(model));

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
      else if (Suspends)
         await Task.Yield();

      return reply;
   }

   public void Dispose() { /* no-op */ }
}
