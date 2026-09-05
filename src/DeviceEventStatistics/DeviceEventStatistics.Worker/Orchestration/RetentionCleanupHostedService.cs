using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class RetentionCleanupHostedService(
    IOperationalCleanupStore cleanupStore,
    ProjectionLeaseCoordinator leaseCoordinator,
    StartupReadinessBarrier readinessBarrier,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<RetentionOptions> retentionOptions,
    TimeProvider timeProvider,
    IStatisticsTelemetry telemetry,
    GracefulShutdownCoordinator shutdownCoordinator,
    ILogger<RetentionCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readinessBarrier.WaitAsync(stoppingToken);
        if (!workerOptions.Value.Enabled ||
            projectionOptions.Value.Mode is ProjectionMode.Bootstrap or ProjectionMode.Backfill or ProjectionMode.Rebuild)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24), timeProvider);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var lease = leaseCoordinator.CurrentLease;
        var acquiredHere = false;
        try
        {
            if (!shutdownCoordinator.TryBeginOperation(out var operation))
            {
                return;
            }

            using (operation)
            {
            if (lease is null)
            {
                var acquired = await leaseCoordinator.TryAcquireAsync(cancellationToken);
                if (!acquired.Acquired || acquired.Lease is null)
                {
                    return;
                }

                lease = acquired.Lease;
                acquiredHere = true;
            }

            var now = timeProvider.GetUtcNow();
            var result = await cleanupStore.CleanupAsync(
                lease.Identity,
                lease,
                now - TimeSpan.FromDays(retentionOptions.Value.ProjectionRunRetentionDays),
                now - TimeSpan.FromDays(retentionOptions.Value.ProjectionRunRetentionDays),
                cancellationToken);
            if (result.DeletedStagingRows > 0 || result.DeletedProjectionRuns > 0)
            {
                logger.LogInformation(
                    StatisticsContractConstants.Messages.MSG_LOG_RETENTION_CLEANUP,
                    result.DeletedStagingRows,
                    result.DeletedProjectionRuns);
            }
            telemetry.RecordOperationalCleanup(
                result.DeletedStagingRows,
                result.DeletedProjectionRuns);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_FAILED);
        }
        finally
        {
            if (acquiredHere)
            {
                await leaseCoordinator.ReleaseAsync(CancellationToken.None);
            }
        }
    }
}
