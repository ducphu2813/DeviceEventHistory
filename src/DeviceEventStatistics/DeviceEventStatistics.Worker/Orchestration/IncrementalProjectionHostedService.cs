using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class IncrementalProjectionHostedService(
    ProjectionLeaseCoordinator leaseCoordinator,
    StatisticsProjectionPipeline pipeline,
    StartupReadinessBarrier readinessBarrier,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
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

        while (!stoppingToken.IsCancellationRequested)
        {
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
                logger.LogError(exception, StatisticsContractConstants.Messages.MSG_LOG_PROJECTION_FAILED);
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
        var coverageStart = settings.CoverageStartAtUtc ??
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_PROJECTION_COVERAGE_START_MISSING);
        var projectionOptions = new IncrementalProjectionOptions(
            new ProjectionIdentity(
                settings.Name,
                settings.ProjectionVersion,
                StatisticsContractConstants.DefaultPartitionKey),
            settings.MappingVersion,
            settings.MetricSetVersion,
            coverageStart,
            settings.BatchSize,
            settings.MaxContributionsPerBatch,
            settings.OverlapWindow,
            settings.ReadSafetyDelay,
            settings.Scope.CompanyIds,
            settings.Scope.DeviceIds);

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await pipeline.ExecutePageAsync(projectionOptions, lease, cancellationToken);
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

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken);
}
