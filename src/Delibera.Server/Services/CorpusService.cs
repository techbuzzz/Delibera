using Delibera.Core.Rag;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

/// <summary>
/// Manages RAG corpora: creation, document ingestion, search, and removal.
/// Delegates to the <see cref="IRagProviderFactory"/> from Delibera.Core.
/// </summary>
public sealed class CorpusService : ICorpusService
{
    private readonly ILogger<CorpusService> _logger;
    private readonly IRagProviderFactory   _ragFactory;
    private readonly IConfiguration        _configuration;

    // In-memory index of corpora metadata (id -> CorpusInfo)
    private readonly Dictionary<string, CorpusInfo> _corpora = new();

    public CorpusService(
        ILogger<CorpusService> logger,
        IRagProviderFactory    ragFactory,
        IConfiguration         configuration)
    {
        _logger        = logger;
        _ragFactory    = ragFactory;
        _configuration = configuration;
    }

    // ── ICorpusService ─────────────────────────────────────────────────────────

    public Task<IReadOnlyCollection<CorpusInfo>> ListCorporaAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<CorpusInfo>>(_corpora.Values.ToList());

    public Task<CorpusInfo> CreateCorpusAsync(CreateCorpusRequest request, CancellationToken ct)
    {
        if (_corpora.ContainsKey(request.CorpusId))
            throw new InvalidOperationException(
                $"Corpus '{request.CorpusId}' already exists.");

        var info = new CorpusInfo
        {
            CorpusId    = request.CorpusId,
            DisplayName = request.DisplayName,
            CreatedAt   = DateTimeOffset.UtcNow
        };

        _corpora[info.CorpusId] = info;
        _logger.LogInformation("Corpus '{CorpusId}' created.", info.CorpusId);
        return Task.FromResult(info);
    }

    public async Task AddDocumentAsync(
        string             corpusId,
        CorpusDocumentDto  doc,
        CancellationToken  ct)
    {
        EnsureExists(corpusId);

        var provider = GetProvider();
        await provider.IndexAsync(corpusId, doc.DocumentId, doc.Content, ct);

        _corpora[corpusId].DocumentCount++;
        _logger.LogInformation(
            "Document '{DocumentId}' indexed into corpus '{CorpusId}'.",
            doc.DocumentId, corpusId);
    }

    public async Task<IReadOnlyCollection<CorpusDocumentMeta>> ListDocumentsAsync(
        string            corpusId,
        CancellationToken ct)
    {
        EnsureExists(corpusId);
        var provider = GetProvider();
        var docs = await provider.ListDocumentsAsync(corpusId, ct);
        return docs.Select(d => new CorpusDocumentMeta
        {
            DocumentId = d.Id,
            Source     = d.Source
        }).ToList();
    }

    public async Task DeleteDocumentAsync(
        string            corpusId,
        string            documentId,
        CancellationToken ct)
    {
        EnsureExists(corpusId);
        var provider = GetProvider();
        await provider.DeleteDocumentAsync(corpusId, documentId, ct);
        _corpora[corpusId].DocumentCount = Math.Max(0, _corpora[corpusId].DocumentCount - 1);
        _logger.LogInformation(
            "Document '{DocumentId}' removed from corpus '{CorpusId}'.",
            documentId, corpusId);
    }

    public async Task<IReadOnlyCollection<RagSearchResult>> SearchAsync(
        string            corpusId,
        string            query,
        int               topK,
        CancellationToken ct)
    {
        EnsureExists(corpusId);
        var provider = GetProvider();
        return await provider.SearchAsync(corpusId, query, topK, ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void EnsureExists(string corpusId)
    {
        if (!_corpora.ContainsKey(corpusId))
            throw new KeyNotFoundException($"Corpus '{corpusId}' not found.");
    }

    private IRagProvider GetProvider()
        => _ragFactory.Create(
            _configuration.GetSection("Delibera:Rag").Get<RagProviderOptions>()
            ?? new RagProviderOptions());
}
