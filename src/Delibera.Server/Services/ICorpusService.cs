using Delibera.Core.Rag;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

public interface ICorpusService
{
    Task<IReadOnlyCollection<CorpusInfo>>          ListCorporaAsync(CancellationToken ct);
    Task<CorpusInfo>                               CreateCorpusAsync(CreateCorpusRequest request, CancellationToken ct);
    Task                                           AddDocumentAsync(string corpusId, CorpusDocumentDto doc, CancellationToken ct);
    Task<IReadOnlyCollection<CorpusDocumentMeta>>  ListDocumentsAsync(string corpusId, CancellationToken ct);
    Task                                           DeleteDocumentAsync(string corpusId, string documentId, CancellationToken ct);
    Task<IReadOnlyCollection<RagSearchResult>>     SearchAsync(string corpusId, string query, int topK, CancellationToken ct);
}
