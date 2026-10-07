using Delibera.Core.Compression;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers.RAG;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///   A debate may be grounded in several corpora, but <see cref="IRagProvider.SearchAsync" />
///   searches one collection at a time and takes no filter. <see cref="KnowledgeKeeper" />
///   therefore searches each requested collection and merges by score.
/// </summary>
/// <remarks>
///   Before this the keeper held a single collection, so a multi-corpus request could only be
///   satisfied by silently grounding the debate in whichever corpus came first.
/// </remarks>
public sealed class KnowledgeKeeperMultiCollectionTests
{
   /// <summary>Vector store that answers differently per collection and records what it was asked for.</summary>
   private sealed class FakeRagProvider : IRagProvider
   {
      private readonly Dictionary<string, IReadOnlyList<VectorSearchResult>> _byCollection;

      public FakeRagProvider(Dictionary<string, IReadOnlyList<VectorSearchResult>> byCollection)
         => _byCollection = byCollection;

      public List<(string Collection, int Limit)> Searches { get; } = [];

      public string ProviderName => "FakeRag";

      public IVectorStore VectorStore => throw new NotSupportedException();

      public IEmbeddingProvider EmbeddingProvider => throw new NotSupportedException();

      public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
         string collection, string query, int limit = 5, float scoreThreshold = 0.0f,
         CancellationToken ct = default)
      {
         Searches.Add((collection, limit));
         return Task.FromResult(
            _byCollection.TryGetValue(collection, out var hits)
               ? hits.Take(limit).ToList()
               : (IReadOnlyList<VectorSearchResult>)[]);
      }

      public Task<string> GetContextAsync(
         string collection, string query, int limit = 5, CancellationToken ct = default) =>
         Task.FromResult(string.Join("\n", _byCollection.TryGetValue(collection, out var hits)
            ? hits.Take(limit).Select(h => h.Text)
            : []));

      public Task<int> IndexDocumentAsync(
         string collection, string text, Dictionary<string, string>? metadata = null,
         int chunkSize = 500, int chunkOverlap = 50, CancellationToken ct = default) =>
         Task.FromResult(1);

      public Task<int> IndexFileAsync(
         string collection, string path, int chunkSize = 500, int chunkOverlap = 50,
         CancellationToken ct = default) => Task.FromResult(1);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
   }

   private static VectorSearchResult Hit(string text, double score) =>
      new(Guid.NewGuid().ToString("N"), text, (float)score, new Dictionary<string, string>());

   private static CouncilMember Member()
   {
      using var factory = new Delibera.Core.Providers.ProviderFactory();
      var llm = factory.CreateOllama("http://localhost:11434");
      return new CouncilMember("keeper-model", llm, "Knowledge Keeper");
   }

   [Fact]
   public async Task Searches_Every_Collection_And_Merges_By_Score()
   {
      var rag = new FakeRagProvider(new Dictionary<string, IReadOnlyList<VectorSearchResult>>
      {
         ["corpus_a"] = [Hit("low from a", 0.30), Hit("lowest from a", 0.10)],
         ["corpus_b"] = [Hit("high from b", 0.90), Hit("mid from b", 0.50)]
      });

      using var factory = new Delibera.Core.Providers.ProviderFactory();
      var llm = factory.CreateOllama("http://localhost:11434");
      var keeper = new KnowledgeKeeper(rag, new CouncilMember("keeper", llm, "Knowledge Keeper"), ["corpus_a", "corpus_b"]);

      var hits = await keeper.SearchKnowledgeAsync("query", limit: 2);

      rag.Searches.Should().HaveCount(2, "both corpora must be consulted");
      rag.Searches.Select(s => s.Collection).Should().BeEquivalentTo(["corpus_a", "corpus_b"]);
      hits.Should().HaveCount(2);
      hits[0].Score.Should().Be(0.90f, "the best hit across corpora must come first");
      hits[1].Score.Should().Be(0.50f);
   }

   [Fact]
   public async Task Single_Collection_Behaviour_Is_Unchanged()
   {
      var rag = new FakeRagProvider(new Dictionary<string, IReadOnlyList<VectorSearchResult>>
      {
         ["corpus_only"] = [Hit("first", 0.90), Hit("second", 0.40), Hit("third", 0.20)]
      });

      using var factory = new Delibera.Core.Providers.ProviderFactory();
      var llm = factory.CreateOllama("http://localhost:11434");
      var keeper = new KnowledgeKeeper(rag, new CouncilMember("keeper", llm, "Knowledge Keeper"), "corpus_only");

      keeper.CollectionName.Should().Be("corpus_only");
      keeper.Collections.Should().ContainSingle();

      var hits = await keeper.SearchKnowledgeAsync("query", limit: 2);

      // Single collection: exactly one search, and the provider's own limit is used unchanged.
      rag.Searches.Should().ContainSingle();
      rag.Searches[0].Limit.Should().Be(2);
      hits.Select(h => h.Text).Should().Equal("first", "second");
   }

   [Fact]
   public void Duplicate_And_Blank_Collections_Are_Collapsed()
   {
      using var factory = new Delibera.Core.Providers.ProviderFactory();
      var llm = factory.CreateOllama("http://localhost:11434");

      var keeper = new KnowledgeKeeper(
         new FakeRagProvider(new Dictionary<string, IReadOnlyList<VectorSearchResult>>()),
         new CouncilMember("keeper", llm, "Knowledge Keeper"),
         ["corpus_a", "corpus_a", "  ", ""]);

      keeper.Collections.Should().Equal("corpus_a");
   }

   [Fact]
   public void At_Least_One_Collection_Is_Required()
   {
      using var factory = new Delibera.Core.Providers.ProviderFactory();
      var llm = factory.CreateOllama("http://localhost:11434");
      var rag = new FakeRagProvider(new Dictionary<string, IReadOnlyList<VectorSearchResult>>());

      var act = () => new KnowledgeKeeper(rag, new CouncilMember("keeper", llm, "Knowledge Keeper"), Array.Empty<string>());

      act.Should().Throw<ArgumentException>();
   }
}