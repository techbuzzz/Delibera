using Serilog.Context;

namespace Delibera.Server.Middleware;

/// <summary>
///    Reads or generates X-Correlation-Id and adds it to the response + log scope.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
   private const string HeaderName = "X-Correlation-Id";

   public async Task InvokeAsync(HttpContext ctx)
   {
      var correlationId = ctx.Request.Headers[HeaderName].FirstOrDefault() ?? Guid.NewGuid().ToString("N");

      ctx.Items[HeaderName] = correlationId;
      ctx.Response.Headers[HeaderName] = correlationId;

      using (LogContext.PushProperty("CorrelationId", correlationId))
      {
         await next(ctx);
      }
   }
}
