namespace Delibera.Server.Api.Contracts;

public sealed record CreateCorpusRequest
{
    public required string Name        { get; init; }
    public          string? Description { get; init; }
    public          string  VectorStore { get; init; } = "qdrant"; // qdrant | pgvector
}

public sealed record IndexDocumentRequest
{
    public required string Content  { get; init; }
    public          string? Title   { get; init; }
    public          string? Source  { get; init; }
    public          Dictionary<string, string>? Metadata { get; init; }
}

public sealed record CorpusDto
{
    public required string CorpusId    { get; init; }
    public required string Name        { get; init; }
    public          string? Description { get; init; }
    public required int    DocumentCount { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record DocumentDto
{
    public required string DocumentId { get; init; }
    public required string Title      { get; init; }
    public required int    Chunks     { get; init; }
    public required DateTimeOffset IndexedAt { get; init; }
}
