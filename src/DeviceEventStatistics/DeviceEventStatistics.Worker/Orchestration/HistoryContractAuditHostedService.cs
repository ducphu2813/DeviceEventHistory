using System.Diagnostics;
using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class HistoryContractAuditHostedService(
    HistoryContractAuditHandler auditHandler,
    IStatisticsBatchWriter batchWriter,
    ProjectionLeaseCoordinator leaseCoordinator,
    StartupReadinessBarrier readinessBarrier,
    ProjectionDefinitionRuntimeState runtimeDefinition,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IStatisticsTelemetry telemetry,
    GracefulShutdownCoordinator shutdownCoordinator,
    ILogger<HistoryContractAuditHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readinessBarrier.WaitAsync(stoppingToken);
        var worker = workerOptions.Value;
        var settings = projectionOptions.Value;
        if (!worker.Enabled || settings.Mode is not ProjectionMode.Incremental)
        {
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["WorkerId"] = worker.WorkerId,
            ["ProjectionName"] = settings.Name,
            ["ProjectionVersion"] = settings.ProjectionVersion,
            ["Mode"] = settings.Mode.ToString(),
            ["Operation"] = "HistoryContractAudit"
        });

        using var timer = new PeriodicTimer(settings.DeepDiscoveryInterval);
        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        if (!shutdownCoordinator.TryBeginOperation(out var operation))
        {
            return;
        }

        using (operation)
        {
            LeaseAcquireResult acquired;
            try
            {
                acquired = await leaseCoordinator.TryAcquireAsync(stoppingToken);
                if (!acquired.Acquired || acquired.Lease is null)
                {
                    return;
                }

                using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    leaseCoordinator.LeaseLostToken);
                try
                {
                    await RunLeaseLoopAsync(acquired.Lease, leaseCancellation.Token);
                }
                catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
                {
                    // Shutdown or fencing ends the bounded audit turn. The cursor advances only after commit.
                }
                finally
                {
                    await leaseCoordinator.ReleaseAsync(CancellationToken.None);
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

    private async Task RunLeaseLoopAsync(
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        var settings = projectionOptions.Value;
        var executionOptions = ProjectionExecutionOptionsFactory.Create(
            settings,
            runtimeDefinition.GetRequired());
        var auditOptions = new AuditRunOptions(
            settings.DeepDiscoveryMaxPages,
            settings.DeepDiscoveryMaxEvents,
            settings.DeepDiscoveryMaxDuration);
        var stopwatch = Stopwatch.StartNew();
        var processedPages = 0;
        var processedEvents = 0;

        while (processedPages < auditOptions.MaxPages &&
               processedEvents < auditOptions.MaxEvents &&
               stopwatch.Elapsed < auditOptions.MaxDuration &&
               !cancellationToken.IsCancellationRequested)
        {
            var pageOptions = executionOptions with
            {
                BatchSize = Math.Min(
                    executionOptions.BatchSize,
                    auditOptions.MaxEvents - processedEvents)
            };
            var prepared = await auditHandler.PreparePageAsync(
                pageOptions,
                lease,
                cancellationToken);
            var pageStopwatch = Stopwatch.StartNew();
            var committed = await batchWriter.PersistAsync(
                prepared.ProjectionPage.Batch,
                cancellationToken);
            processedPages++;
            processedEvents += prepared.ProjectionPage.ReadEventCount;

            telemetry.RecordBatchCommitted(
                ProjectionMode.Incremental.ToString(),
                new ProjectionPageResult(
                    committed,
                    prepared.ProjectionPage.ReadEventCount,
                    prepared.IsComplete),
                pageStopwatch.Elapsed);
            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_BATCH_COMMITTED,
                prepared.ProjectionPage.ReadEventCount,
                committed.NewEventCount,
                committed.DuplicateEventCount,
                committed.AffectedRowCount,
                committed.DataRevision);

            if (prepared.IsComplete)
            {
                break;
            }

            if (prepared.ProjectionPage.ReadEventCount == 0)
            {
                throw new InvalidOperationException(
                    StatisticsContractConstants.Messages.MSG_AUDIT_PAGE_INVALID);
            }
        }
    }

    private sealed record AuditRunOptions(
        int MaxPages,
        int MaxEvents,
        TimeSpan MaxDuration);
}
