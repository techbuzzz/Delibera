using Delibera.Core.Interfaces;
using Delibera.Core.Providers.RAG;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Delibera.Core.Tests;

/// <summary>
///    W2-15 — indexing was not idempotent. Every chunk received a freshly generated point id
///    (<c>Guid.NewGuid()</c>), so re-indexing the same document never matched an existing row:
///    the store appended instead of replacing. Three runs over the same 24 unique chunks left
///    72 points in the collection, and the duplicates diluted every subsequent search.
///
///    Both concrete stores use the point id as an upsert key (Qdrant point id, pgvector
///    <c>ON CONFLICT (id) DO UPDATE</c>), so deriving the id from the chunk's identity makes
///    re-indexing a replace. These tests pin that behaviour without touching a live vector DB.
/// </summary>
public sealed class BaseRagProviderIdempotencyTests
{
   [Fact]
   public async Task IndexFileAsync_Indexing_The_same_file_twice_does_not_duplicate_points()
   {
      var store = new RecordingVectorStore();
      var provider = CreateProvider(store);
      var path = WriteTempFile("Paragraph one about databases. Paragraph two about caching. Paragraph three about retrieval.");

      try
      {
         var first = await provider.IndexFileAsync("docs", path, chunkSize: 40, chunkOverlap: 5);
         first.Should().BeGreaterThan(1, "the fixture must produce several chunks to be meaningful");

         var afterFirst = await store.CountAsync("docs");
         var second = await provider.IndexFileAsync("docs", path, chunkSize: 40, chunkOverlap: 5);

         second.Should().Be(first);
         (await store.CountAsync("docs")).Should().Be(afterFirst,
            "re-indexing the same file must replace its points rather than append a second copy");
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public async Task IndexFileAsync_After_an_edit_does_not_grow_the_collection()
   {
      var store = new RecordingVectorStore();
      var provider = CreateProvider(store);
      var path = WriteTempFile("The council should ship the feature. The council should ship the docs. The council should ship the tests.");

      try
      {
         var indexedFirst = await provider.IndexFileAsync("docs", path, chunkSize: 45, chunkOverlap: 5);
         var before = await store.CountAsync("docs");

         // A same-length replacement keeps the chunk boundaries — and therefore the chunk count —
         // identical, so a position-derived id must overwrite in place.
         await File.WriteAllTextAsync(path, "The board should ship the feature. The council should ship the docs. The council should ship the tests.");

         var indexedSecond = await provider.IndexFileAsync("docs", path, chunkSize: 45, chunkOverlap: 5);

         indexedSecond.Should().Be(indexedFirst);
         (await store.CountAsync("docs")).Should().Be(before,
            "an edited chunk replaces its own point instead of orphaning it");
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public async Task IndexDocumentAsync_Without_source_metadata_Dedupes_Identical_Text()
   {
      var store = new RecordingVectorStore();
      var provider = CreateProvider(store);
      var text = "Anonymous chunk one. Anonymous chunk two.";

      await provider.IndexDocumentAsync("anon", text, null, chunkSize: 20, chunkOverlap: 5);
      var afterFirst = await store.CountAsync("anon");

      await provider.IndexDocumentAsync("anon", text, null, chunkSize: 20, chunkOverlap: 5);

      (await store.CountAsync("anon")).Should().Be(afterFirst,
         "with no source identity the id falls back to the chunk's own content hash");
   }

   [Fact]
   public async Task IndexDocumentAsync_Emits_Ids_That_Survive_the_stores_Guid_parsing()
   {
      var store = new RecordingVectorStore();
      var provider = CreateProvider(store);

      await provider.IndexFileAsync("docs", WriteTempFile("One. Two. Three."), chunkSize: 10, chunkOverlap: 2);

      store.ObservedIds.Should().NotBeEmpty();
      store.ObservedIds.Should().AllSatisfy(id => Guid.TryParse(id, out _).Should().BeTrue(
         "both concrete stores silently substitute a fresh GUID when the supplied id does not parse, "
         + "which would reintroduce exactly the duplication this fix removes"));
   }

   [Fact]
   public async Task IndexFileAsync_Warns_When_the_collection_predates_deterministic_ids()
   {
      var store = new RecordingVectorStore();
      var logger = new CapturingLogger();
      var provider = CreateProvider(store, logger);
      var path = WriteTempFile("Only one short paragraph here.");

      try
      {
         // Simulate a collection left behind by Delibera 10.5.0, where every run appended.
         await store.SeedLegacyPoints("docs", 5);

         await provider.IndexFileAsync("docs", path, chunkSize: 200, chunkOverlap: 10);

         logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning,
            "the operator needs to be told that delete-and-reindex is required to clear the stale points");
         logger.Entries.Should().Contain(e => e.Message.Contains("DeleteCollectionAsync"));
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public async Task IndexFileAsync_Distinct_Sources_With_The_Same_File_Name_Do_Not_Collide()
   {
      var store = new RecordingVectorStore();
      var provider = CreateProvider(store);
      var dirA = Directory.CreateTempSubdirectory("delibera-a");
      var dirB = Directory.CreateTempSubdirectory("delibera-b");
      var fileA = Path.Combine(dirA.FullName, "notes.md");
      var fileB = Path.Combine(dirB.FullName, "notes.md");

      try
      {
         await File.WriteAllTextAsync(fileA, "Content that lives in the first folder entirely.");
         await File.WriteAllTextAsync(fileB, "Content that lives in the second folder entirely.");

         await provider.IndexFileAsync("docs", fileA, chunkSize: 60, chunkOverlap: 10);
         var afterA = await store.CountAsync("docs");
         await provider.IndexFileAsync("docs", fileB, chunkSize: 60, chunkOverlap: 10);

         (await store.CountAsync("docs")).Should().BeGreaterThan(afterA,
            "the full path, not the bare file name, is the source identity");
      }
      finally
      {
         File.Delete(fileA);
         File.Delete(fileB);
         dirA.Delete(recursive: true);
         dirB.Delete(recursive: true);
      }
   }

   private static QdrantRagProvider CreateProvider(RecordingVectorStore store, ILogger? logger = null)
      => new(store, new StubEmbeddingProvider(), logger);

   private static string WriteTempFile(string content)
   {
      var path = Path.Combine(Path.GetTempPath(), $"delibera-w2-15-{Guid.NewGuid():N}.txt");
      File.WriteAllText(path, content);
      return path;
   }

   /// <summary>
   ///    Vector store that keys points exactly the way the concrete stores do: an id that fails
   ///    <see cref="Guid.TryParse(string, out Guid)" /> is replaced by a random one. Mirroring that
   ///    branch here is what makes the id-format test meaningful without a live Qdrant or Postgres.
   /// </summary>
   private sealed class RecordingVectorStore : IVectorStore
   {
      private readonly Dictionary<string, Dictionary<string, VectorPoint>> _collections = [];

      public List<string> ObservedIds { get; } = [];

      public string StoreName => "recording";

      public Task EnsureCollectionAsync(string collectionName, int vectorSize, CancellationToken ct = default)
      {
         Collection(collectionName);
         return Task.CompletedTask;
      }

      public Task UpsertAsync(string collectionName, IReadOnlyList<VectorPoint> points, CancellationToken ct = default)
      {
         var collection = Collection(collectionName);
         foreach (var point in points)
         {
            var effectiveId = Guid.TryParse(point.Id, out var guid)
               ? guid.ToString()
               : Guid.NewGuid().ToString();

            ObservedIds.Add(effectiveId);
            collection[effectiveId] = point;
         }

         return Task.CompletedTask;
      }

      private Dictionary<string, VectorPoint> Collection(string collectionName)
      {
         if (!_collections.TryGetValue(collectionName, out var collection))
         {
            collection = [];
            _collections[collectionName] = collection;
         }

         return collection;
      }

      public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
         string collectionName,
         float[] queryVector,
         int limit = 5,
         float scoreThreshold = 0.0f,
         CancellationToken ct = default)
         => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);

      public Task DeleteCollectionAsync(string collectionName, CancellationToken ct = default)
      {
         _collections.Remove(collectionName);
         return Task.CompletedTask;
      }

      public Task<long> CountAsync(string collectionName, CancellationToken ct = default)
         => Task.FromResult((long)(_collections.TryGetValue(collectionName, out var c) ? c.Count : 0));

      public Task SeedLegacyPoints(string collectionName, int count)
      {
         var collection = Collection(collectionName);
         for (var i = 0; i < count; i++)
         {
            collection[Guid.NewGuid().ToString()] = new VectorPoint(
               Guid.NewGuid().ToString(), [0f], $"legacy {i}", new Dictionary<string, string>());
         }

         return Task.CompletedTask;
      }

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
   }

   private sealed class StubEmbeddingProvider : IEmbeddingProvider
   {
      public string EmbeddingModelName => "stub-embed";

      public int VectorSize => 4;

      public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
         => Task.FromResult(new[] { (float)text.Length, 1f, 0f, 0f });

      public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
         => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => new[] { (float)t.Length, 1f, 0f, 0f }).ToList());
   }

   private sealed class CapturingLogger : ILogger
   {
      public List<(LogLevel Level, string Message)> Entries { get; } = [];

      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
         LogLevel logLevel,
         EventId eventId,
         TState state,
         Exception? exception,
         Func<TState, Exception?, string> formatter)
         => Entries.Add((logLevel, formatter(state, exception)));
   }
}