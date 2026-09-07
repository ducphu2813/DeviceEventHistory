using System.Diagnostics;
using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.Projection;
using DeviceEventStatistics.Domain.State;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class ReconciliationHostedService(
    ReconciliationCoordinator coordinator,
    IReconciliationRequestStore requestStore,
    IProjectionScopeReader scopeReader,
    ProjectionLeaseCoordinator leaseCoordinator,
    StartupReadinessBarrier readinessBarrier,
    ProjectionDefinitionRuntimeState runtimeDefinition,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<ReconciliationOptions> reconciliationOptions,
    IOptions<RetentionOptions> retentionOptions,
    TimeProvider timeProvider,
    LocalStatisticsDateResolver dateResolver,
    IStatisticsTelemetry telemetry,
    GracefulShutdownCoordinator shutdownCoordinator,
    ILogger<ReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readinessBarrier.WaitAsync(stoppingToken);
        if (!workerOptions.Value.Enabled ||
            !reconciliationOptions.Value.Enabled ||
            projectionOptions.Value.Mode is not (ProjectionMode.Incremental or ProjectionMode.Reconciliation))
        {
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["WorkerId"] = workerOptions.Value.WorkerId,
            ["ProjectionName"] = projectionOptions.Value.Name,
            ["ProjectionVersion"] = projectionOptions.Value.ProjectionVersion,
            ["Mode"] = projectionOptions.Value.Mode.ToString()
        });

        using var timer = new PeriodicTimer(reconciliationOptions.Value.ScheduleInterval, timeProvider);
        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var settings = projectionOptions.Value;
        var definition = runtimeDefinition.GetRequired();
        var identity = definition.Identity;
        if (!shutdownCoordinator.TryBeginOperation(out var operation))
        {
            return;
        }

        using (operation)
        {
            try
            {
                var acquired = await leaseCoordinator.TryAcquireAsync(cancellationToken);
                if (!acquired.Acquired || acquired.Lease is null)
                {
                    return;
                }

                var lease = acquired.Lease;
                using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    leaseCoordinator.LeaseLostToken);
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    await EnqueueRollingRequestsAsync(identity, lease, leaseCancellation.Token);
                    var options = new ReconciliationExecutionOptions(
                        definition.MappingVersion,
                        definition.MetricSetVersion,
                        settings.LeaseDuration,
                        settings.RetryMinDelay,
                        reconciliationOptions.Value.MaxAttempts,
                        settings.BatchSize,
                        reconciliationOptions.Value.MaxRangeDays,
                        reconciliationOptions.Value.MaxRequestsPerRun,
                        TimeSpan.FromDays(retentionOptions.Value.MongoHistoryRetentionDays),
                        TimeSpan.FromDays(retentionOptions.Value.MinimumHistoryHeadroomDays),
                        dateResolver.Resolve(timeProvider.GetUtcNow()).StatisticsDate);
                    var result = await coordinator.RunOnceAsync(
                        identity,
                        lease,
                        options,
                        leaseCancellation.Token);
                    telemetry.RecordReconciliation(result, stopwatch.Elapsed);
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
                catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
                {
                    return;
                }
                finally
                {
                    await leaseCoordinator.ReleaseAsync(CancellationToken.None);
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
        }
    }

    private async Task EnqueueRollingRequestsAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        var scope = projectionOptions.Value.Scope;
        var deviceKeys = await scopeReader.ReadDeviceKeysAsync(
            identity,
            scope.CompanyIds,
            scope.DeviceIds,
            cancellationToken);
        if (deviceKeys.Count == 0)
        {
            return;
        }

        var today = dateResolver.Resolve(timeProvider.GetUtcNow()).StatisticsDate;
        var from = today.AddDays(-(reconciliationOptions.Value.RollingDays - 1));
        var requestedAt = timeProvider.GetUtcNow();
        var requests = deviceKeys
            .SelectMany(key => StateTypes.Supported
                .OrderBy(stateType => stateType, StringComparer.Ordinal)
                .Select(stateType => new ReconciliationRequestSeed(
                    identity,
                    new StateStreamKey(key.CompanyId, key.DeviceId, stateType),
                    from,
                    today,
                    ReconciliationReasonCodes.RollingSchedule,
                    requestedAt,
                    new string('0', 64))))
            .ToArray();
        await requestStore.EnqueueAsync(requests, lease, cancellationToken);
    }
}
