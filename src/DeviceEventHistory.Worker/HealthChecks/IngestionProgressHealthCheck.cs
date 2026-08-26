using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Domain.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.HealthChecks;

public sealed class IngestionProgressHealthCheck(
    IngestionHealthState healthState,
    IOptions<Configuration.WorkerOptions> workerOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!workerOptions.Value.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                AppConst.Observability.HealthWorkerDisabledDescription));
        }

        var snapshot = healthState.Snapshot;
        if (!snapshot.IsLive)
        {
            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                AppConst.Observability.HealthIngestionNotLiveDescription));
        }

        var status = snapshot.Status switch
        {
            IngestionHealthStatus.Ready => HealthStatus.Healthy,
            IngestionHealthStatus.Degraded => HealthStatus.Degraded,
            IngestionHealthStatus.Unhealthy => HealthStatus.Unhealthy,
            _ => HealthStatus.Degraded
        };

        return Task.FromResult(new HealthCheckResult(
            status,
            AppConst.Messages.Format(
                AppConst.Observability.HealthIngestionStatusDescription,
                snapshot.Status,
                snapshot.Reason)));
    }
}
