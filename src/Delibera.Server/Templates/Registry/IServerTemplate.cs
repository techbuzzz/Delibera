using Delibera.Core.Interfaces;

namespace Delibera.Server.Templates.Registry;

/// <summary>
/// Contract that every council template registered in the server must implement.
/// </summary>
public interface IServerTemplate
{
    string   TemplateId       { get; }
    string   DisplayName      { get; }
    string?  Description      { get; }
    string   Strategy         { get; }   // StandardDebate | CritiqueDebate | ConsensusDebate
    int      DefaultMaxRounds { get; }
    string[] MemberRoles      { get; }
    string   VotingStrategy   { get; }   // Majority | BordaCount | Weighted
    bool     RagEnabled       { get; }
    bool     OperatorEnabled  { get; }

    /// <summary>
    /// Configure and return a <see cref="ICouncilBuilder"/> ready to be built
    /// for a specific debate request.
    /// </summary>
    ICouncilBuilder Configure(
        CreateDebateRequest    request,
        IServiceProvider       services,
        IConfiguration         configuration);
}
