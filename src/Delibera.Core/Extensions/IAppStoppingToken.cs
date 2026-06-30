namespace Delibera.Core.Extensions;

/// <summary>
///    Minimal abstraction over a host's "shutdown" signal. Lets consumers plug in
///    any cancellation source (ASP.NET Core's <c>IHostApplicationLifetime</c>,
///    a worker-service lifetime, a custom one) without forcing a hosting dependency
///    on the core library.
/// </summary>
public interface IAppStoppingToken
{
   /// <summary>
   ///    Token that is canceled when the host is shutting down (e.g. SIGINT,
   ///    app stop request, container termination).
   /// </summary>
   CancellationToken ApplicationStopping { get; }
}
