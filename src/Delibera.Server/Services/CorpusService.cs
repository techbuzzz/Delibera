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

    // corpusId → (meta, documents)
    private readonly Dictionary<string, (CorpusDto Meta, List<DocumentDto> Docs)> _store = new();

    public CorpusService(ILogger<CorpusService> logger)
        => _logger = logger;

    // ── ICorpusService ────────────────────────────────────────────────────────

    public IReadOnlyCollection<CorpusDto> ListCorpora()
        => _store.Values.Select(v => v.Meta).ToList();

    public CorpusDto CreateCorpus(CreateCorpusRequest request)
    {
        var id = Guid.NewGuid().ToString("N")[..12];

        if (_store.Values.Any(v =>
                string.Equals(v.Meta.Name, request.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Corpus with name '{request.Name}' already exists.");

        var dto = new CorpusDto
        {
            CorpusId      = id,
            Name          = request.Name,
            Description   = request.Description,
            DocumentCount = 0,
            CreatedAt     = DateTimeOffset.UtcNow,
        };

        _store[id] = (dto, []);
        _logger.LogInformation("Corpus '{CorpusId}' ({Name}) created.", id, request.Name);
        return dto;
    }

    public Task<DocumentDto?> IndexDocumentAsync(
        string             corpusId,
        IndexDocumentRequest request,
        CancellationToken  ct)
    {
        if (!_store.TryGetValue(corpusId, out var entry))
            return Task.FromResult<DocumentDto?>(null);

        var doc = new DocumentDto
        {
            DocumentId = Guid.NewGuid().ToString("N")[..12],
            Title      = request.Title ?? "(untitled)",
            Chunks     = EstimateChunks(request.Content),
            IndexedAt  = DateTimeOffset.UtcNow,
        };

        entry.Docs.Add(doc);

        // Replace meta with incremented DocumentCount (record is immutable)
        _store[corpusId] = (entry.Meta with { DocumentCount = entry.Docs.Count }, entry.Docs);

        _logger.LogInformation(
            "Document '{DocumentId}' indexed into corpus '{CorpusId}'.",
            doc.DocumentId, corpusId);

        return Task.FromResult<DocumentDto?>(doc);
    }

    public DocumentDto[]? ListDocuments(string corpusId)
        => _store.TryGetValue(corpusId, out var entry)
            ? entry.Docs.ToArray()
            : null;

    public void DeleteDocument(string corpusId, string documentId)
    {
        if (!_store.TryGetValue(corpusId, out var entry)) return;

        var removed = entry.Docs.RemoveAll(d => d.DocumentId == documentId);
        if (removed > 0)
        {
            _store[corpusId] = (entry.Meta with { DocumentCount = entry.Docs.Count }, entry.Docs);
            _logger.LogInformation(
                "Document '{DocumentId}' removed from corpus '{CorpusId}'.",
                documentId, corpusId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Rough chunk estimate: ~512 tokens per chunk, ~0.75 tokens per word.</summary>
    private static int EstimateChunks(string content)
    {
        var words  = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var tokens = (int)(words / 0.75);
        return Math.Max(1, (int)Math.Ceiling(tokens / 512.0));
    }
}
