using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class DurationRefreshHostedService(
    IDurationRefreshStore refreshStore,
    ProjectionLeaseCoordinator leaseCoordinator,
    StartupReadinessBarrier readinessBarrier,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<StateOptions> stateOptions,
    TimeProvider timeProvider,
    GracefulShutdownCoordinator shutdownCoordinator,
    ILogger<DurationRefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readinessBarrier.WaitAsync(stoppingToken);
        if (!workerOptions.Value.Enabled ||
            !stateOptions.Value.Enabled ||
            projectionOptions.Value.Mode is not ProjectionMode.Incremental)
        {
            return;
        }

        using var timer = new PeriodicTimer(stateOptions.Value.RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var lease = leaseCoordinator.CurrentLease;
            if (lease is null)
            {
                continue;
            }

            try
            {
                if (!shutdownCoordinator.TryBeginOperation(out var operation))
                {
                    return;
                }

                using (operation)
                {
                var affectedRows = await refreshStore.RefreshAsync(
                    lease.Identity,
                    lease,
                    timeProvider.GetUtcNow(),
                    stateOptions.Value.RefreshPageSize,
                    stoppingToken);
                if (affectedRows > 0)
                {
                    logger.LogInformation(
                        StatisticsContractConstants.Messages.MSG_LOG_STATE_REFRESH_COMPLETED,
                        affectedRows);
                }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_FAILED);
            }
        }
    }
}
