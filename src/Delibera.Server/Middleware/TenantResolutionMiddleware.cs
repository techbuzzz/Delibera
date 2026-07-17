using Delibera.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace Delibera.Server.Middleware;

/// <summary>
/// Resolves tenant from X-Tenant-Id header and stores it in HttpContext.Items.
/// </summary>
public sealed class TenantResolutionMiddleware(
   RequestDelegate next,
   IOptions<DeliberaServerOptions> options)
{
   private const string HeaderName = "X-Tenant-Id";
   public const string ItemKey = "TenantId";

   public async Task InvokeAsync(HttpContext ctx)
   {
      var tenantId = ctx.Request.Headers[HeaderName].FirstOrDefault() ?? options.Value.DefaultTenantId;
      ctx.Items[ItemKey] = tenantId;
      await next(ctx);
   }

   public static string Resolve(HttpContext ctx)
      => ctx.Items.TryGetValue(ItemKey, out var v) && v is string s
         ? s
         : "default";
}
