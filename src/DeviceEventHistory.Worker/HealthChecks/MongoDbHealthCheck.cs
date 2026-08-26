using DeviceEventHistory.Infrastructure.MongoDb;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Domain.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.HealthChecks;

public sealed class MongoDbHealthCheck(
    MongoDbContext mongoContext,
    IngestionHealthState healthState,
    IOptions<Configuration.WorkerOptions> workerOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!workerOptions.Value.Enabled)
        {
            return HealthCheckResult.Healthy(AppConst.Observability.HealthWorkerDisabledDescription);
        }

        try
        {
            await mongoContext.PingAsync(cancellationToken);
            healthState.MarkMongoAvailable();
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            healthState.MarkMongoUnavailable();
            var snapshot = healthState.Snapshot;
            var status = snapshot.Status == IngestionHealthStatus.Unhealthy
                ? HealthStatus.Unhealthy
                : HealthStatus.Degraded;
            return new HealthCheckResult(
                status,
                AppConst.Observability.HealthMongoUnavailableDescription,
                exception);
        }
    }
}
