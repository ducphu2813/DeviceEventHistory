using Microsoft.Extensions.Diagnostics.HealthChecks;
using DeviceEventStatistics.Worker.Configuration;

namespace DeviceEventStatistics.Worker.HealthChecks;

public sealed class StartupReadinessHealthCheck(StartupReadinessState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (state.IsReady)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy(state.IsDisabled ? "Statistics worker is disabled." : "Startup preflight completed."));
        }

        return Task.FromResult(
            HealthCheckResult.Unhealthy(
                $"Statistics worker is not ready. FailureCode={state.FailureCode ?? "STAT-STARTUP-NOT-COMPLETED"}"));
    }
}
