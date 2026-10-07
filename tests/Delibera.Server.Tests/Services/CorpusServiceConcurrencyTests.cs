using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delibera.Server.Tests.Services;

/// <summary>
///    W4-07 — <c>CorpusService</c> is a singleton, so its state is touched by every request
///    thread at once.
///    <para>
///    It used to hold a plain <c>Dictionary</c> and a shared <c>List&lt;DocumentDto&gt;</c>,
///    and indexing was a read-append-write: <c>ListCorpora</c> enumerated the dictionary while
///    a request added to it, and two simultaneous indexers both read the same list and both
///    wrote back a version missing the other's document.
///    </para>
/// </summary>
public sealed class CorpusServiceConcurrencyTests
{
    private static CorpusService NewService() => new(NullLogger<CorpusService>.Instance);

    [Fact]
    public async Task Concurrent_Indexing_Keeps_Every_Document()
    {
        var service = NewService();
        var corpus = service.CreateCorpus(new CreateCorpusRequest { Name = "handbook" });

        const int writers = 8;
        const int perWriter = 25;

        await Task.WhenAll(Enumerable.Range(0, writers).Select(w => Task.Run(async () =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                await service.IndexDocumentAsync(
                    corpus.CorpusId,
                    new IndexDocumentRequest { Content = $"document {w}-{i}", Title = $"doc {w}-{i}" },
                    CancellationToken.None);
            }
        })));

        var documents = service.ListDocuments(corpus.CorpusId);

        documents.Should().NotBeNull();
        documents!.Length.Should().Be(writers * perWriter,
            "a lost update here means a document the caller was told was stored is simply gone");

        var meta = service.ListCorpora().Single(c => c.CorpusId == corpus.CorpusId);
        meta.DocumentCount.Should().Be(writers * perWriter,
            "the counter is derived from the same copy-on-write array, so it cannot drift");
    }

    [Fact]
    public async Task Listing_While_Indexing_Does_Not_Throw()
    {
        var service = NewService();
        var corpus = service.CreateCorpus(new CreateCorpusRequest { Name = "handbook" });

        using var stop = new CancellationTokenSource();
        var errors = new List<Exception>();

        var reader = Task.Run(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                    _ = service.ListCorpora();
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        });

        await Task.WhenAll(Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
                await service.IndexDocumentAsync(
                    corpus.CorpusId,
                    new IndexDocumentRequest { Content = $"doc {w}-{i}" },
                    CancellationToken.None);
        })));

        stop.Cancel();
        await reader;

        errors.Should().BeEmpty("enumerating a dictionary that is being written to throws");
    }

    [Fact]
    public async Task Concurrent_Index_And_Delete_Converge()
    {
        var service = NewService();
        var corpus = service.CreateCorpus(new CreateCorpusRequest { Name = "handbook" });

        var indexed = new List<string>();

        for (var i = 0; i < 10; i++)
        {
            var doc = await service.IndexDocumentAsync(
                corpus.CorpusId, new IndexDocumentRequest { Content = "seed" }, CancellationToken.None);
            indexed.Add(doc!.DocumentId);
        }

        // Remove half while the rest are still being added.
        var toRemove = indexed.Take(5).ToArray();
        var removals = toRemove
            .Select(id => Task.Run(() => service.DeleteDocument(corpus.CorpusId, id)));
        var additions = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
                await service.IndexDocumentAsync(
                    corpus.CorpusId, new IndexDocumentRequest { Content = $"more {w}-{i}" },
                    CancellationToken.None);
        }));

        await Task.WhenAll(removals.Concat(additions));

        var documents = service.ListDocuments(corpus.CorpusId)!;
        var remainingIds = documents.Select(d => d.DocumentId).ToHashSet();

        // IntersectWith mutates in place and returns void, so assert on the set afterwards.
        remainingIds.IntersectWith(toRemove);
        remainingIds.Should().BeEmpty("the deleted documents stay deleted");
        documents.Length.Should().Be(5 + 80);

        var meta = service.ListCorpora().Single(c => c.CorpusId == corpus.CorpusId);
        meta.DocumentCount.Should().Be(documents.Length,
            "the count must describe the documents actually stored");
    }

    [Fact]
    public void Duplicate_Corpus_Names_Are_Rejected_Case_Insensitively()
    {
        var service = NewService();
        service.CreateCorpus(new CreateCorpusRequest { Name = "Handbook" });

        var act = () => service.CreateCorpus(new CreateCorpusRequest { Name = "handbook" });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Concurrent_Creates_Of_The_Same_Name_Yield_Exactly_One()
    {
        // The uniqueness check used to be "scan the values, then insert" — two requests
        // could both pass the scan. It is now claimed atomically through TryAdd.
        var service = NewService();
        var succeeded = 0;
        var rejected = 0;

        Parallel.For(0, 32, _ =>
        {
            try
            {
                service.CreateCorpus(new CreateCorpusRequest { Name = "handbook" });
                Interlocked.Increment(ref succeeded);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref rejected);
            }
        });

        succeeded.Should().Be(1);
        rejected.Should().Be(31);
        service.ListCorpora().Should().HaveCount(1);
    }

    [Fact]
    public void DeleteDocument_For_An_Unknown_Id_Is_A_No_Op()
    {
        var service = NewService();
        var corpus = service.CreateCorpus(new CreateCorpusRequest { Name = "handbook" });

        var act = () => service.DeleteDocument(corpus.CorpusId, "not-a-document");

        act.Should().NotThrow();
        service.ListDocuments(corpus.CorpusId).Should().BeEmpty();
    }
}
