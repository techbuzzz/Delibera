using System.Collections.Concurrent;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

/// <summary>
///    In-memory corpus registry that indexes documents into the configured vector store.
/// </summary>
/// <remarks>
///    <para>
///       The previous version only counted estimated chunks and kept the DTO in memory, so
///       <c>POST /corpora/{id}/documents</c> answered <c>{ chunks: 1 }</c> while Qdrant stayed
///       empty — indexing was reported but never performed.
///    </para>
///    <para>
///       When RAG is disabled the service degrades to the old behaviour (metadata only) rather
///       than failing, so the corpus API stays usable without a vector store.
///    </para>
/// </remarks>
public sealed class CorpusService : ICorpusService
{
   private readonly ILogger<CorpusService> _logger;
   private readonly ServerRagProviderFactory? _rag;

   /// <summary>
   ///    corpusId → (meta, documents).
   /// </summary>
   /// <remarks>
   ///    This service is a singleton, so the store is a <see cref="ConcurrentDictionary{TKey,TValue}" />
   ///    rather than a plain dictionary: <c>ListCorpora</c> enumerated the backing dictionary
   ///    while requests were adding to it. Documents are copy-on-write — an entry is replaced
   ///    with a new one through <c>AddOrUpdate</c> — so a reader always sees a whole,
   ///    self-consistent version and two concurrent indexers cannot lose an update the way
   ///    the previous <c>list.Add</c> followed by a dictionary write did.
   /// </remarks>
   private readonly ConcurrentDictionary<string, CorpusEntry> _store = new();

   /// <summary>
   ///    Lower-cased corpus name → corpus id. Claimed with <c>TryAdd</c> so that "the name
   ///    must be unique" is enforced by the dictionary itself rather than by a check that
   ///    two concurrent requests could both pass.
   /// </summary>
   private readonly ConcurrentDictionary<string, string> _namesByLowerCase = new(StringComparer.Ordinal);

   public CorpusService(ILogger<CorpusService> logger, ServerRagProviderFactory? rag = null)
   {
      _logger = logger;
      _rag = rag;
   }

   // ── ICorpusService ────────────────────────────────────────────────────────

   public IReadOnlyCollection<CorpusDto> ListCorpora()
      => _store.Values.Select(v => v.Meta).ToList();

   public CorpusDto CreateCorpus(CreateCorpusRequest request)
   {
      var id = Guid.NewGuid().ToString("N")[..12];
      var nameKey = request.Name.ToLowerInvariant();

      if (!_namesByLowerCase.TryAdd(nameKey, id))
         throw new InvalidOperationException(
            $"Corpus with name '{request.Name}' already exists.");

      var dto = new CorpusDto
      {
         CorpusId = id,
         Name = request.Name,
         Description = request.Description,
         DocumentCount = 0,
         CreatedAt = DateTimeOffset.UtcNow,
      };

      _store[id] = new CorpusEntry(dto, []);
      _logger.LogInformation("Corpus '{CorpusId}' ({Name}) created.", id, request.Name);
      return dto;
   }

   public async Task<DocumentDto?> IndexDocumentAsync(
      string corpusId,
      IndexDocumentRequest request,
      CancellationToken ct)
   {
      if (!_store.TryGetValue(corpusId, out var existing))
         return null;

      var doc = new DocumentDto
      {
         DocumentId = Guid.NewGuid().ToString("N")[..12],
         Title = request.Title ?? "(untitled)",
         Chunks = 0,
         IndexedAt = DateTimeOffset.UtcNow,
      };

      // Real indexing: chunk, embed and store. The chunk count reported on the way out is what the
      // vector store actually holds, not an estimate of what it might hold.
      var rag = _rag?.Get();
      if (rag is not null)
      {
         var collection = ServerRagProviderFactory.CollectionForCorpus(corpusId);
         var metadata = new Dictionary<string, string>
         {
            ["corpusId"] = corpusId,
            ["documentId"] = doc.DocumentId,
            ["title"] = doc.Title,
         };

         if (request.Source is { Length: > 0 } source)
            metadata["source"] = source;

         try
         {
            doc = doc with
            {
               Chunks = await rag.IndexDocumentAsync(collection, request.Content, metadata, ct: ct)
                  .ConfigureAwait(false)
            };
         }
         catch (Exception ex)
         {
            // Report the failure rather than a phantom success: a document the caller believes is
            // indexed but that no debate can retrieve is worse than an explicit error.
            _logger.LogError(ex, "Indexing document '{Title}' into corpus '{CorpusId}' failed.", doc.Title, corpusId);
            throw new InvalidOperationException(
               $"Indexing '{doc.Title}' into corpus '{corpusId}' failed: {ex.Message}", ex);
         }
      }
      else
      {
         doc = doc with { Chunks = EstimateChunks(request.Content) };
         _logger.LogWarning(
            "RAG is disabled; document '{DocumentId}' was recorded as metadata only and is not retrievable.",
            doc.DocumentId);
      }

      // Compare-and-swap rather than "read, append, write back": the previous version lost
      // a document whenever two requests indexed at the same moment, because both read the
      // same list and both wrote back a version missing the other's addition.
      while (_store.TryGetValue(corpusId, out var entry))
      {
         var documents = new DocumentDto[entry.Documents.Length + 1];
         Array.Copy(entry.Documents, documents, entry.Documents.Length);
         documents[^1] = doc;

         var updated = new CorpusEntry(
            entry.Meta with { DocumentCount = documents.Length },
            documents);

         if (_store.TryUpdate(corpusId, updated, entry))
            break;
         // Another writer replaced the entry first — re-read and try again.
      }

      _logger.LogInformation(
         "Document '{DocumentId}' indexed into corpus '{CorpusId}' as {Chunks} chunk(s){Note}.",
         doc.DocumentId, corpusId, doc.Chunks, rag is null ? " (metadata only, RAG disabled)" : string.Empty);

      return doc;
   }

   public DocumentDto[]? ListDocuments(string corpusId)
      => _store.TryGetValue(corpusId, out var entry) ? entry.Documents : null;

   public void DeleteDocument(string corpusId, string documentId)
   {
      while (_store.TryGetValue(corpusId, out var entry))
      {
         var remaining = entry.Documents.Where(d => d.DocumentId != documentId).ToArray();
         if (remaining.Length == entry.Documents.Length)
            return; // the document was not there — nothing to log, nothing to write

         var updated = new CorpusEntry(
            entry.Meta with { DocumentCount = remaining.Length },
            remaining);

         if (!_store.TryUpdate(corpusId, updated, entry))
            continue; // lost the race; re-read and re-evaluate

         _logger.LogInformation(
            "Document '{DocumentId}' removed from corpus '{CorpusId}'.",
            documentId, corpusId);
         return;
      }
   }

   // ── Helpers ───────────────────────────────────────────────────────────────

   /// <summary>A corpus and its documents, replaced wholesale on every mutation.</summary>
   private sealed record CorpusEntry(CorpusDto Meta, DocumentDto[] Documents);

   /// <summary>Rough chunk estimate: ~512 tokens per chunk, ~0.75 tokens per word.</summary>
   private static int EstimateChunks(string content)
   {
      var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
      var tokens = (int)(words / 0.75);
      return Math.Max(1, (int)Math.Ceiling(tokens / 512.0));
   }
}
