using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    W2-02 — the Knowledge Keeper used to perform the same RAG search twice per round:
///    once directly and once more through <c>GetContextAsync</c>, which searches again
///    internally for the same collection, the same query and the same limit. Every round
///    therefore paid for a duplicate embedding call and a duplicate vector-store walk.
/// </summary>
public sealed class KnowledgeKeeperRagTests
{
    [Fact]
    public async Task ProvideContextForRoundAsync_Searches_The_Store_Exactly_Once()
    {
        var rag = new CountingRagProvider();
        var keeper = new KnowledgeKeeper(
            rag,
            new CouncilMember("fake-model", new FakeLLMProvider(), "Knowledge Keeper"),
            "kb");

        var context = await keeper.ProvideContextForRoundAsync("Should we ship?", 1);

        rag.SearchCallCount.Should().Be(1,
            "the round already holds the search results; a second search is pure overhead");
        context.Sources.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProvideContextForRoundAsync_Does_Not_Call_GetContextAsync()
    {
        var rag = new CountingRagProvider();
        var keeper = new KnowledgeKeeper(
            rag,
            new CouncilMember("fake-model", new FakeLLMProvider(), "Knowledge Keeper"),
            "kb");

        await keeper.ProvideContextForRoundAsync("Should we ship?", 1);

        rag.GetContextCallCount.Should().Be(0,
            "GetContextAsync re-searches, so calling it here duplicates the embedding work");
    }

    [Fact]
    public async Task ProvideContextForRoundAsync_Still_Sends_The_Retrieved_Evidence_To_The_Model()
    {
        // Halving the retrieval is only correct if the hits still reach the model.
        var rag = new CountingRagProvider();
        var model = new RecordingProvider("answer");
        var keeper = new KnowledgeKeeper(
            rag,
            new CouncilMember("fake-model", model, "Knowledge Keeper"),
            "kb");

        var context = await keeper.ProvideContextForRoundAsync("Should we ship?", 1);

        context.Answer.Should().Be("answer");
        context.Sources.Should().ContainSingle(s => s.Text == "Retrieved evidence");
        model.LastUserPrompt.Should().Contain("Retrieved evidence");
        model.LastUserPrompt.Should().Contain("Should we ship?");
    }

    /// <summary>Captures the last prompt so the test can assert what the model received.</summary>
    private sealed class RecordingProvider(string reply) : ILLMProvider
    {
        public string? LastUserPrompt { get; private set; }

        public string ProviderName => "Recording";

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["fake-model"]);

        public Task<ModelCapabilities> GetModelCapabilitiesAsync(
            string model, CancellationToken ct = default)
            => Task.FromResult(ModelCapabilities.Unknown(model));

        public Task<string> ChatAsync(
            string model, string systemPrompt, string userPrompt,
            float temperature = 0.7f, CancellationToken ct = default)
        {
            LastUserPrompt = userPrompt;
            return Task.FromResult(reply);
        }

        public void Dispose() { }
    }

    /// <summary>
    ///    Minimal <see cref="IRagProvider" /> that counts calls instead of hitting a
    ///    vector store, so a duplicated retrieval is observable.
    /// </summary>
    private sealed class CountingRagProvider : IRagProvider
    {
        public int SearchCallCount { get; private set; }

        public int GetContextCallCount { get; private set; }

        public CountingEmbeddingProvider Embedding { get; } = new();

        public string ProviderName => "Counting";

        public IVectorStore VectorStore { get; } = new NullVectorStore();

        public IEmbeddingProvider EmbeddingProvider => Embedding;

        public Task<int> IndexDocumentAsync(
            string collectionName, string documentText, Dictionary<string, string>? metadata = null,
            int chunkSize = 500, int chunkOverlap = 50, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> IndexFileAsync(
            string collectionName, string filePath, int chunkSize = 500,
            int chunkOverlap = 50, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string collectionName, string query, int limit = 5, float scoreThreshold = 0.0f,
            CancellationToken ct = default)
        {
            SearchCallCount++;
            IReadOnlyList<VectorSearchResult> hits =
            [
                new VectorSearchResult("1", "Retrieved evidence", 0.9f)
            ];
            return Task.FromResult(hits);
        }

        public Task<string> GetContextAsync(
            string collectionName, string query, int limit = 5, CancellationToken ct = default)
        {
            GetContextCallCount++;
            return Task.FromResult("[Source 1] Retrieved evidence");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        public int EmbedCallCount { get; private set; }

        public string EmbeddingModelName => "counting";

        public int VectorSize => 4;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            EmbedCallCount++;
            return Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(
                texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToList());
    }

    private sealed class NullVectorStore : IVectorStore
    {
        public string StoreName => "null";

        public Task EnsureCollectionAsync(string collectionName, int vectorSize, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertAsync(string collectionName, IReadOnlyList<VectorPoint> points, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string collectionName, float[] queryVector, int limit = 5,
            float scoreThreshold = 0.0f, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);

        public Task DeleteCollectionAsync(string collectionName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<long> CountAsync(string collectionName, CancellationToken ct = default)
            => Task.FromResult(0L);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
