namespace Delibera.Core.Extensions;

/// <summary>
///    Extension methods that make <see cref="Interfaces.ICouncilExecutor"/>
///    cooperate with host-level shutdown signals so a council is cancelled
///    automatically when the host stops.
/// </summary>
public static class CouncilExecutorLifetimeExtensions
{
   /// <summary>
   ///    Executes the council, linking the caller's <paramref name="ct"/>
   ///    with the <see cref="IAppStoppingToken.ApplicationStopping"/> token
   ///    so that a host shutdown cancels the debate cooperatively.
   ///    Whichever signal fires first wins.
   /// </summary>
   /// <param name="executor">The council executor.</param>
   /// <param name="lifetime">The lifetime abstraction to link to.</param>
   /// <param name="ct">An additional cancellation token from the caller.</param>
   /// <returns>The completed debate result.</returns>
   /// <exception cref="ArgumentNullException"><paramref name="executor"/> or <paramref name="lifetime"/> is null.</exception>
   /// <exception cref="OperationCanceledException">Either token was canceled.</exception>
   public static async Task<Models.DebateResult> ExecuteAsync(
      this Interfaces.ICouncilExecutor executor,
      IAppStoppingToken lifetime,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(executor);
      ArgumentNullException.ThrowIfNull(lifetime);

      using var linked = CancellationTokenSource.CreateLinkedTokenSource(
         ct, lifetime.ApplicationStopping);
      return await executor.ExecuteAsync(linked.Token).ConfigureAwait(false);
   }
}
