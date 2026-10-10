using Delibera.Core;
using Serilog.Context;

namespace Delibera.Server.Middleware;

/// <summary>
///    Reads or generates X-Correlation-Id and adds it to the response + log scope.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
   public async Task InvokeAsync(HttpContext ctx)
   {
      var correlationId = ctx.Request.Headers[BuiltIn.HttpHeaders.CorrelationId].FirstOrDefault() ?? Guid.NewGuid().ToString("N");

      ctx.Items[BuiltIn.HttpHeaders.CorrelationId] = correlationId;
      ctx.Response.Headers[BuiltIn.HttpHeaders.CorrelationId] = correlationId;

      using (LogContext.PushProperty("CorrelationId", correlationId))
      {
         await next(ctx);
      }
   }
}
