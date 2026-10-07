namespace Delibera.Core.Interfaces;

/// <summary>
///    Supplies the tools a member may call while producing its response.
/// </summary>
/// <remarks>
///    A provider is a catalogue, not an executor: it hands over
///    <see cref="AIFunction" /> instances, and the debate pipeline decides when to invoke them.
///    That split keeps argument binding, result formatting and the iteration bound in one place
///    rather than in every provider.
/// </remarks>
public interface IToolProvider
{
   /// <summary>Human-readable name of this source, used in logs and <c>ToolCallLog</c> entries.</summary>
   string Name { get; }

   /// <summary>The tools this provider exposes to a member.</summary>
   /// <param name="ct">Cancellation token.</param>
   ValueTask<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default);
}

/// <summary>
///    One tool invocation made by a member during a debate round.
/// </summary>
/// <param name="MemberName">Display name of the member that called the tool.</param>
/// <param name="ToolName">Name of the invoked tool.</param>
/// <param name="Arguments">Raw arguments as supplied by the model.</param>
/// <param name="Result">Text result handed back to the member.</param>
/// <param name="Succeeded">Whether the invocation completed without throwing.</param>
/// <param name="ErrorMessage">Failure detail when <paramref name="Succeeded" /> is <c>false</c>.</param>
/// <param name="Transport">
///    How the call reached the model — native function calling where the provider supports it,
///    otherwise the marker protocol.
/// </param>
/// <param name="Duration">Wall time spent in the tool.</param>
/// <param name="RoundNumber">Round in which the call happened.</param>
public sealed record ToolCallLog(
   string MemberName,
   string ToolName,
   string Arguments,
   string Result,
   bool Succeeded,
   string? ErrorMessage,
   ToolCallTransport Transport,
   TimeSpan Duration,
   int RoundNumber);

/// <summary>
///    How a tool call was expressed to the model.
/// </summary>
public enum ToolCallTransport
{
   /// <summary>
   ///    Provider-native function calling via <c>Microsoft.Extensions.AI</c>. Used when the
   ///    member's provider wraps a real <c>IChatClient</c>.
   /// </summary>
   Native = 0,

   /// <summary>
   ///    The <c>[[TOOL: name {json}]]</c> marker parsed out of the response text. Used for
   ///    providers that only return plain strings.
   /// </summary>
   Marker = 1
}