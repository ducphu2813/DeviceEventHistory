using System.Diagnostics;
using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class IncrementalProjectionHostedService(
    ProjectionLeaseCoordinator leaseCoordinator,
    StatisticsProjectionPipeline pipeline,
    StartupReadinessBarrier readinessBarrier,
    ProjectionDefinitionRuntimeState runtimeDefinition,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    TimeProvider timeProvider,
    IStatisticsTelemetry telemetry,
    GracefulShutdownCoordinator shutdownCoordinator,
    ILogger<IncrementalProjectionHostedService> logger) : BackgroundService
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
            ["Mode"] = settings.Mode.ToString()
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!shutdownCoordinator.TryBeginOperation(out var acquireOperation))
            {
                return;
            }

            LeaseAcquireResult acquired;
            try
            {
                acquired = await leaseCoordinator.TryAcquireAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_FAILED);
                await DelayAsync(settings.PollInterval, stoppingToken);
                continue;
            }
            finally
            {
                acquireOperation!.Dispose();
            }

            if (!acquired.Acquired || acquired.Lease is null)
            {
                logger.LogDebug(StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_LEASE_UNAVAILABLE);
                await DelayAsync(settings.PollInterval, stoppingToken);
                continue;
            }

            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_LEASE_ACQUIRED,
                acquired.Lease.Epoch,
                acquired.Lease.ExpiresAtUtc);
            using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                leaseCoordinator.LeaseLostToken);
            try
            {
                await RunLeaseLoopAsync(acquired.Lease, settings, leaseCancellation.Token);
            }
            catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
            {
                // Shutdown or a fenced lease ends the current loop. The outer loop decides whether to reacquire.
            }
            catch (Exception exception)
            {
                telemetry.RecordBatchFailed(settings.Mode.ToString());
                logger.LogError(exception, StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_FAILED);
                logger.LogWarning(
                    StatisticsContractConstants.Messages.MSG_LOG_BATCH_RETRY,
                    settings.Mode);
                await DelayAsync(settings.PollInterval, stoppingToken);
            }
            finally
            {
                await leaseCoordinator.ReleaseAsync(CancellationToken.None);
            }
        }
    }

    private async Task RunLeaseLoopAsync(
        ProjectionLeaseToken lease,
        ProjectionOptions settings,
        CancellationToken cancellationToken)
    {
        var projectionOptions = ProjectionExecutionOptionsFactory.Create(
            settings,
            runtimeDefinition.GetRequired());

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!shutdownCoordinator.TryBeginOperation(out var operation))
            {
                return;
            }

            ProjectionPageResult result;
            var stopwatch = Stopwatch.StartNew();
            using (operation)
            {
                result = await pipeline.ExecutePageAsync(projectionOptions, lease, cancellationToken);
            }

            telemetry.RecordBatchCommitted(settings.Mode.ToString(), result, stopwatch.Elapsed);
            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_BATCH_COMMITTED,
                result.ReadEventCount,
                result.Commit.NewEventCount,
                result.Commit.DuplicateEventCount,
                result.Commit.AffectedRowCount,
                result.Commit.DataRevision);
            if (result.IsCaughtUp)
            {
                await DelayAsync(settings.PollInterval, cancellationToken);
            }
        }
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, timeProvider, cancellationToken);
}
