using Delibera.Core.Models;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

/// <summary>
///    In-memory runtime record for a single debate lifecycle.
///    Holds status, result, and metadata. Round streaming is handled
///    via <see cref="IDebateOrchestrator.StreamAsync" />, not this record.
/// </summary>
public sealed class DebateRecord
{
    public required string      DebateId   { get; init; }
    public required string      TemplateId { get; init; }
    public required string      TenantId   { get; init; }
    public          DebateStatus Status    { get; set; } = DebateStatus.Pending;
    public          DebateResult? Result  { get; set; }
    public          List<DebateRound> Rounds { get; } = [];
    public          string?     ErrorMessage { get; set; }
    public          DateTimeOffset CreatedAt   { get; } = DateTimeOffset.UtcNow;
    public          DateTimeOffset? CompletedAt { get; set; }
    public          string      Label { get; set; } = string.Empty;
}