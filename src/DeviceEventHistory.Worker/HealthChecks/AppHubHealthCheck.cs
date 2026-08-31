using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.HealthChecks;

public sealed class AppHubHealthCheck(
    IOptions<Configuration.WorkerOptions> workerOptions,
    IOptions<AppHubOptions> appHubOptions,
    AppHubHealthState healthState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!workerOptions.Value.Enabled || !appHubOptions.Value.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                AppConst.Observability.HealthWorkerDisabledDescription));
        }

        var snapshot = healthState.Snapshot;
        var status = snapshot.Status switch
        {
            AppHubHealthStatus.Running => HealthStatus.Healthy,
            AppHubHealthStatus.Unhealthy => HealthStatus.Unhealthy,
            _ => HealthStatus.Degraded
        };

        return Task.FromResult(new HealthCheckResult(
            status,
            AppConst.Messages.Format(
                AppConst.Observability.HealthAppHubStatusDescription,
                snapshot.Status,
                snapshot.Reason)));
    }
}
