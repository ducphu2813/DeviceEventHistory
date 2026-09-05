using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.State;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class ReconciliationHostedService(
    ReconciliationCoordinator coordinator,
    IReconciliationRequestStore requestStore,
    ProjectionLeaseCoordinator leaseCoordinator,
    StartupReadinessBarrier readinessBarrier,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<ReconciliationOptions> reconciliationOptions,
    IOptions<RetentionOptions> retentionOptions,
    TimeProvider timeProvider,
    LocalStatisticsDateResolver dateResolver,
    ILogger<ReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readinessBarrier.WaitAsync(stoppingToken);
        if (!workerOptions.Value.Enabled ||
            !reconciliationOptions.Value.Enabled ||
            projectionOptions.Value.Mode is not ProjectionMode.Reconciliation)
        {
            return;
        }

        using var timer = new PeriodicTimer(reconciliationOptions.Value.ScheduleInterval);
        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var settings = projectionOptions.Value;
        var identity = new ProjectionIdentity(
            settings.Name,
            settings.ProjectionVersion,
            StatisticsContractConstants.DefaultPartitionKey);
        var lease = leaseCoordinator.CurrentLease;
        var acquiredHere = false;
        try
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

            await EnqueueRollingRequestsAsync(identity, lease, cancellationToken);
            var options = new ReconciliationExecutionOptions(
                settings.MappingVersion,
                settings.MetricSetVersion,
                settings.LeaseDuration,
                settings.RetryMinDelay,
                reconciliationOptions.Value.MaxAttempts,
                settings.BatchSize,
                reconciliationOptions.Value.MaxRangeDays,
                reconciliationOptions.Value.MaxRequestsPerRun,
                TimeSpan.FromDays(retentionOptions.Value.MongoHistoryRetentionDays),
                TimeSpan.FromDays(retentionOptions.Value.MinimumHistoryHeadroomDays),
                dateResolver.Resolve(timeProvider.GetUtcNow()).StatisticsDate);
            var result = await coordinator.RunOnceAsync(identity, lease, options, cancellationToken);
            if (result.CompletedCount > 0 || result.RetriedCount > 0 || result.FailedCount > 0)
            {
                logger.LogInformation(
                    StatisticsContractConstants.Messages.MSG_LOG_RECONCILIATION_CYCLE,
                    result.CompletedCount,
                    result.RetriedCount,
                    result.FailedCount,
                    result.CompletedAtUtc);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, StatisticsContractConstants.Messages.MSG_RECONCILIATION_RUN_FAILED);
        }
        finally
        {
            if (acquiredHere)
            {
                await leaseCoordinator.ReleaseAsync(CancellationToken.None);
            }
        }
    }

    private async Task EnqueueRollingRequestsAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        var scope = projectionOptions.Value.Scope;
        if (scope.CompanyIds.Count == 0 || scope.DeviceIds.Count == 0)
        {
            return;
        }

        var today = dateResolver.Resolve(timeProvider.GetUtcNow()).StatisticsDate;
        var from = today.AddDays(-(reconciliationOptions.Value.RollingDays - 1));
        var requestedAt = timeProvider.GetUtcNow();
        var requests = Enumerable.Repeat(0, 1)
            .SelectMany(_ =>
                scope.CompanyIds.SelectMany(companyId =>
                    scope.DeviceIds.SelectMany(deviceId =>
                        StateTypes.Supported.Select(stateType => new ReconciliationRequestSeed(
                            identity,
                            new StateStreamKey(companyId, deviceId, stateType),
                            from,
                            today,
                            ReconciliationReasonCodes.RollingSchedule,
                            requestedAt,
                            new string('0', 64))))))
            .ToArray();
        await requestStore.EnqueueAsync(requests, lease, cancellationToken);
    }
}
