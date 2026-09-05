using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.State;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class ManualProjectionHostedService(
    ReconciliationCoordinator coordinator,
    IReconciliationRequestStore requestStore,
    ProjectionLeaseCoordinator leaseCoordinator,
    StartupReadinessBarrier readinessBarrier,
    ProjectionDefinitionRuntimeState runtimeDefinition,
    IHostApplicationLifetime applicationLifetime,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<ReconciliationOptions> reconciliationOptions,
    IOptions<RetentionOptions> retentionOptions,
    TimeProvider timeProvider,
    LocalStatisticsDateResolver dateResolver,
    GracefulShutdownCoordinator shutdownCoordinator,
    ILogger<ManualProjectionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readinessBarrier.WaitAsync(stoppingToken);
        var settings = projectionOptions.Value;
        if (!workerOptions.Value.Enabled ||
            settings.Mode is not (ProjectionMode.Bootstrap or ProjectionMode.Backfill or ProjectionMode.Rebuild))
        {
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["WorkerId"] = workerOptions.Value.WorkerId,
            ["ProjectionName"] = settings.Name,
            ["ProjectionVersion"] = settings.ProjectionVersion,
            ["Mode"] = settings.Mode.ToString()
        });

        var leaseResult = await leaseCoordinator.TryAcquireAsync(stoppingToken);
        if (!leaseResult.Acquired || leaseResult.Lease is null)
        {
            applicationLifetime.StopApplication();
            return;
        }

        if (!shutdownCoordinator.TryBeginOperation(out var operation))
        {
            await leaseCoordinator.ReleaseAsync(CancellationToken.None);
            applicationLifetime.StopApplication();
            return;
        }

        using (operation)
        try
        {
            var range = ResolveRange(settings);
            var definition = runtimeDefinition.GetRequired();
            var identity = definition.Identity;
            if (settings.Mode is (ProjectionMode.Bootstrap or ProjectionMode.Rebuild) &&
                definition.LifecycleStatus is ProjectionLifecycleStatuses.Ready or ProjectionLifecycleStatuses.Active)
            {
                logger.LogInformation(
                    StatisticsContractConstants.Messages.MSG_LOG_MANUAL_MODE_SKIPPED,
                    identity.ProjectionVersion);
                return;
            }

            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_MANUAL_MODE_STARTED,
                settings.Mode,
                identity.ProjectionVersion,
                range.From,
                range.To);
            var reasonCode = settings.Mode switch
            {
                ProjectionMode.Bootstrap => ReconciliationReasonCodes.Bootstrap,
                ProjectionMode.Backfill => ReconciliationReasonCodes.Backfill,
                ProjectionMode.Rebuild => ReconciliationReasonCodes.Rebuild,
                _ => ReconciliationReasonCodes.InvalidRequest
            };
            var requestedAt = timeProvider.GetUtcNow();
            var requests = settings.Scope.CompanyIds
                .SelectMany(companyId => settings.Scope.DeviceIds
                    .SelectMany(deviceId => StateTypes.Supported.Select(stateType =>
                        new ReconciliationRequestSeed(
                            identity,
                            new StateStreamKey(companyId, deviceId, stateType),
                            range.From,
                            range.To,
                            reasonCode,
                            requestedAt,
                            new string('0', 64)))))
                .ToArray();
            await requestStore.EnqueueAsync(requests, leaseResult.Lease, stoppingToken);

            var runOptions = new ReconciliationExecutionOptions(
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
            var aggregate = await RunUntilDrainedAsync(identity, leaseResult.Lease, runOptions, stoppingToken);
            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_MANUAL_MODE_COMPLETED,
                settings.Mode,
                aggregate.CompletedCount,
                aggregate.RetriedCount,
                aggregate.FailedCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, StatisticsContractConstants.Messages.MSG_RECONCILIATION_RUN_FAILED);
        }
        finally
        {
            await leaseCoordinator.ReleaseAsync(CancellationToken.None);
            applicationLifetime.StopApplication();
        }
    }

    private async Task<ReconciliationRunResult> RunUntilDrainedAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        ReconciliationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        var retried = 0;
        var failed = 0;
        while (true)
        {
            var result = await coordinator.RunOnceAsync(identity, lease, options, cancellationToken);
            completed += result.CompletedCount;
            retried += result.RetriedCount;
            failed += result.FailedCount;
            if (result.CompletedCount == 0 || result.RetriedCount > 0 || result.FailedCount > 0)
            {
                return new ReconciliationRunResult(completed, retried, failed, result.CompletedAtUtc);
            }
        }
    }

    private (DateOnly From, DateOnly To) ResolveRange(ProjectionOptions settings)
    {
        var manualRange = settings.ManualRange;
        if (manualRange.FromUtc is not DateTimeOffset fromUtc ||
            manualRange.ToUtc is not DateTimeOffset toUtc)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_RECONCILIATION_REQUEST_INVALID);
        }

        var from = dateResolver.Resolve(fromUtc).StatisticsDate;
        var to = dateResolver.Resolve(toUtc.AddTicks(-1)).StatisticsDate;
        var retentionBoundary = dateResolver.Resolve(
            timeProvider.GetUtcNow() - TimeSpan.FromDays(retentionOptions.Value.MongoHistoryRetentionDays) +
            TimeSpan.FromDays(retentionOptions.Value.MinimumHistoryHeadroomDays)).StatisticsDate;
        if (from < retentionBoundary)
        {
            throw new ReconciliationCoverageException(ReconciliationReasonCodes.SourceRetentionGap);
        }

        return (from, to);
    }
}
