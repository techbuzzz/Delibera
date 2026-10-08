using Microsoft.AspNetCore.Diagnostics;

namespace Delibera.Server.Infrastructure;

/// <summary>
///    Turns an unhandled exception into an RFC 7807 <c>ProblemDetails</c> response.
/// </summary>
/// <remarks>
///    Without a handler the pipeline returned a bare 500 with no body, and — because this
///    app is built with <c>CreateSlimBuilder</c>, which does not add the developer exception
///    page — the client got nothing it could parse. The default
///    <c>UseExceptionHandler()</c> only produces ProblemDetails outside Development, so the
///    behaviour would still differ by environment; writing it here makes it uniform.
///    <para>
///       The exception message is included in <c>detail</c> only in Development. Stack traces
///       are never part of the payload — they belong in the log, where the correlation id
///       makes them findable.
///    </para>
/// </remarks>
public sealed class ProblemDetailsExceptionHandler(
   IProblemDetailsService problemDetailsService,
   IHostEnvironment environment,
   ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
   /// <summary>Header the correlation id is echoed on, and the key it is stored under.</summary>
   public const string CorrelationIdHeader = "X-Correlation-Id";

   public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext,
      Exception exception,
      CancellationToken cancellationToken)
   {
      // A malformed body or a missing `required` member surfaces as
      // BadHttpRequestException while the parameter is being bound. That is a client
      // error: reporting it as 500 tells the caller the server broke. It has to be
      // claimed here — returning false lets the exception keep travelling and the server
      // render a 500 anyway, because the bind happens downstream of this middleware.
      var isClientError = exception is BadHttpRequestException;
      var status = isClientError
         ? StatusCodes.Status400BadRequest
         : StatusCodes.Status500InternalServerError;

      // A response that already started cannot be rewritten; let the server handle it.
      if (httpContext.Response.HasStarted)
      {
         logger.LogError(exception, "Unhandled exception after the response started.");
         return false;
      }

      var correlationId = httpContext.Items.TryGetValue(CorrelationIdHeader, out var value)
                          && value is string id
         ? id
         : httpContext.TraceIdentifier;

      if (isClientError)
         logger.LogWarning(
            exception,
            "Rejected malformed request for {Method} {Path} (CorrelationId: {CorrelationId}).",
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId);
      else
         logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path} (CorrelationId: {CorrelationId}).",
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId);

      httpContext.Response.StatusCode = status;

      var problem = new ProblemDetails
      {
         Status = status,
         Title = isClientError
            ? "The request could not be read."
            : "An unexpected error occurred.",
         // Detail is the one place an internal message could reach a client, so it is
         // limited to the development environment.
         Detail = environment.IsDevelopment() ? exception.Message : null,
         Instance = httpContext.Request.Path
      };

      // Lets a caller quote the id and find the matching entry in the log.
      problem.Extensions["correlationId"] = correlationId;

      return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
      {
         HttpContext = httpContext,
         ProblemDetails = problem,
         Exception = exception
      });
   }
}
