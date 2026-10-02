using Delibera.Core.Interfaces;
using Delibera.Core.Models;

namespace Delibera.Server.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ILLMProvider"/> that requires no network access.
/// Optionally delays each call so cancellation paths can be exercised.
/// </summary>
/// <remarks>
/// Suspends before completing by default, for the same reason as the Core test fake: a
/// provider that completes synchronously hides any code that is only correct while nothing
/// has suspended. See <c>tests/Delibera.Core.Tests/Fakes/FakeLLMProvider.cs</c> for the
/// incident that motivated it.
/// </remarks>
public sealed class FakeLLMProvider(
    string providerName = "Fake",
    string reply        = "fake-reply",
    int    chatDelayMs  = 0,
    bool   suspends     = true) : ILLMProvider
{
    public int    ChatCallCount { get; private set; }
    public string ProviderName  => providerName;

    /// <summary>Whether this provider suspends before completing.</summary>
    public bool Suspends { get; } = suspends;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(new[] { "fake-model" });

    public async Task<string> ChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        float  temperature   = 0.7f,
        CancellationToken ct = default)
    {
        ChatCallCount++;
        if (chatDelayMs > 0)
            await Task.Delay(chatDelayMs, ct);
        else if (Suspends)
            await Task.Yield();
        return reply;
    }

    public Task<ModelCapabilities> GetModelCapabilitiesAsync(string model, CancellationToken ct = default)
        => Task.FromResult(ModelCapabilities.Unknown(model));

    public void Dispose() { /* no-op */ }
}
