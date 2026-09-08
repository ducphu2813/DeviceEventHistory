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

        using var timer = new PeriodicTimer(retentionOptions.Value.CleanupInterval, timeProvider);
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
                new OperationalCleanupOptions(
                    now - TimeSpan.FromDays(retentionOptions.Value.ProcessedEventRetentionDays),
                    now - TimeSpan.FromDays(retentionOptions.Value.StagingRetentionDays),
                    now - TimeSpan.FromDays(retentionOptions.Value.ProjectionRunRetentionDays),
                    now - TimeSpan.FromDays(retentionOptions.Value.ResolvedFailureRetentionDays),
                    now - TimeSpan.FromDays(retentionOptions.Value.CompletedReconciliationRetentionDays),
                    retentionOptions.Value.CleanupBatchSize),
                cancellationToken);
            if (result.DeletedProcessedEvents > 0 ||
                result.DeletedStagingRows > 0 ||
                result.DeletedProjectionRuns > 0 ||
                result.DeletedResolvedFailures > 0 ||
                result.DeletedCompletedReconciliationRequests > 0)
            {
                logger.LogInformation(
                    StatisticsContractConstants.Messages.MSG_LOG_RETENTION_CLEANUP,
                    result.DeletedProcessedEvents,
                    result.DeletedStagingRows,
                    result.DeletedProjectionRuns,
                    result.DeletedResolvedFailures,
                    result.DeletedCompletedReconciliationRequests);
            }
            telemetry.RecordOperationalCleanup(result);
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
