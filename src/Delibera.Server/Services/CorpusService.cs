using System.Collections.Concurrent;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

/// <summary>
///    In-memory corpus registry. Stores corpus and document metadata;
///    actual vector indexing is delegated to the RAG provider configured
///    in appsettings (Qdrant / pgvector) — wired up when RAG is enabled.
/// </summary>
public sealed class CorpusService : ICorpusService
{
   private readonly ILogger<CorpusService> _logger;

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

   public CorpusService(ILogger<CorpusService> logger)
      => _logger = logger;

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

   public Task<DocumentDto?> IndexDocumentAsync(
      string corpusId,
      IndexDocumentRequest request,
      CancellationToken ct)
   {
      if (!_store.ContainsKey(corpusId))
         return Task.FromResult<DocumentDto?>(null);

      var doc = new DocumentDto
      {
         DocumentId = Guid.NewGuid().ToString("N")[..12],
         Title = request.Title ?? "(untitled)",
         Chunks = EstimateChunks(request.Content),
         IndexedAt = DateTimeOffset.UtcNow,
      };

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
         "Document '{DocumentId}' indexed into corpus '{CorpusId}'.",
         doc.DocumentId, corpusId);

      return Task.FromResult<DocumentDto?>(doc);
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
