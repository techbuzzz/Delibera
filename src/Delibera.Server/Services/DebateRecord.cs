using Delibera.Core.Models;
using Delibera.Server.Api.Contracts;

namespace Delibera.Server.Services;

/// <summary>In-memory runtime record for a single debate lifecycle.</summary>
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

    // SSE channel: completed rounds are broadcast to streaming consumers
    private readonly System.Threading.Channels.Channel<DebateRound> _channel =
        System.Threading.Channels.Channel.CreateUnbounded<DebateRound>();

    public System.Threading.Channels.ChannelWriter<DebateRound>  RoundWriter => _channel.Writer;
    public System.Threading.Channels.ChannelReader<DebateRound>  RoundReader => _channel.Reader;
}
