using Microsoft.Extensions.Diagnostics.HealthChecks;

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
        var count = templates.Count;
        return Task.FromResult(
            count > 0
                ? HealthCheckResult.Healthy($"{count} template(s) registered.")
                : HealthCheckResult.Degraded("No templates registered."));
    }
}
