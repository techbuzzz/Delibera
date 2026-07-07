using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

/// <summary>
///    Manages in-memory RAG corpus metadata and delegates document
///    operations to the configured vector store at runtime.
/// </summary>
public interface ICorpusService
{
    // ── Corpus CRUD ───────────────────────────────────────────────────────────
    IReadOnlyCollection<CorpusDto>  ListCorpora();
    CorpusDto                       CreateCorpus(CreateCorpusRequest request);

    // ── Document operations ───────────────────────────────────────────────────
    Task<DocumentDto?>              IndexDocumentAsync(string corpusId, IndexDocumentRequest request, CancellationToken ct);
    DocumentDto[]?                  ListDocuments(string corpusId);
    void                            DeleteDocument(string corpusId, string documentId);
}
