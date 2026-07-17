using Microsoft.Extensions.Diagnostics.HealthChecks;
using Delibera.Server.Services;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Infrastructure;

/// <summary>Basic health check — verifies Delibera services are resolvable.</summary>
public sealed class DeliberaCoreHealthCheck(
   ITemplateRegistry templates,
   IDebateOrchestrationService orchestration) : IHealthCheck
{
   public Task<HealthCheckResult> CheckHealthAsync(
      HealthCheckContext context,
      CancellationToken cancellationToken = default)
   {
      // Suppress unused-parameter warning; orchestration presence confirms DI is wired.
      _ = orchestration;

      var count = templates.Count;
      return Task.FromResult(
         count > 0
            ? HealthCheckResult.Healthy($"{count} template(s) registered.")
            : HealthCheckResult.Degraded("No templates registered."));
   }
}
