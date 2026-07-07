namespace Delibera.Server.Api.Contracts;

public sealed record TemplateDto
{
    public required string   TemplateId   { get; init; }
    public required string   DisplayName  { get; init; }
    public          string?  Description  { get; init; }
    public required string   Strategy     { get; init; }
    public required int      DefaultMaxRounds { get; init; }
    public required string[] MemberRoles  { get; init; }
    public required string   VotingStrategy { get; init; }
    public          bool     RagEnabled   { get; init; }
    public          bool     OperatorEnabled { get; init; }
}
