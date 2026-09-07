using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DeviceEventStatistics.Worker.HealthChecks;

public sealed class ProcessLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy());
}
