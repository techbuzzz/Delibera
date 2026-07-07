namespace Delibera.Server.Api.Contracts;

/// <summary>SSE payload for one debate round.</summary>
public sealed record DebateRoundDto
{
    public required int    RoundNumber    { get; init; }
    public required bool   IsFinal        { get; init; }
    public required string Strategy       { get; init; }
    public required ParticipantMessageDto[] Messages { get; init; }
    public          string?  ChairmanSummary  { get; init; }
    public          OperatorInteractionDto[]? OperatorInteractions { get; init; }
}

public sealed record ParticipantMessageDto
{
    public required string Role      { get; init; }
    public required string Content   { get; init; }
    public          string? ModelName { get; init; }
}

public sealed record OperatorInteractionDto
{
    public required string Task   { get; init; }
    public required string Result { get; init; }
}
