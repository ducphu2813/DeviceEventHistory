using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using DeviceEventStatistics.Worker.HealthChecks;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class OperationalHealthHostedService(
    IProjectionOperationalSnapshotReader snapshotReader,
    StatisticsHealthEvaluator evaluator,
    OperationalHealthState healthState,
    StartupReadinessBarrier readinessBarrier,
    StartupReadinessState readinessState,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<ObservabilityOptions> observabilityOptions,
    TimeProvider timeProvider,
    GracefulShutdownCoordinator shutdownCoordinator,
    ILogger<OperationalHealthHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readinessBarrier.WaitAsync(stoppingToken);
        if (!workerOptions.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(observabilityOptions.Value.HealthCheckInterval);
        do
        {
            await EvaluateAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = projectionOptions.Value;
            var identity = new ProjectionIdentity(
                settings.Name,
                settings.ProjectionVersion,
                StatisticsContractConstants.DefaultPartitionKey);
            var snapshot = await snapshotReader.ReadAsync(
                identity,
                workerOptions.Value.WorkerId,
                cancellationToken);
            var evaluation = evaluator.Evaluate(
                new StatisticsHealthInput(
                    readinessState.IsReady,
                    true,
                    shutdownCoordinator.IsDraining,
                    timeProvider.GetUtcNow(),
                    snapshot),
                observabilityOptions.Value.LagWarningAfter,
                observabilityOptions.Value.LagViolationAfter);
            var previous = healthState.Evaluation;
            healthState.Set(evaluation);
            if (previous?.Status != evaluation.Status ||
                !string.Equals(previous.Reason, evaluation.Reason, StringComparison.Ordinal))
            {
                logger.LogInformation(
                    StatisticsContractConstants.Messages.MSG_LOG_HEALTH_STATUS_CHANGED,
                    evaluation.Status,
                    evaluation.Reason,
                    evaluation.IncrementalLag,
                    evaluation.PendingRequestAge,
                    evaluation.RetentionHeadroom);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            healthState.SetDependencyFailure(exception);
            logger.LogError(
                exception,
                StatisticsContractConstants.Messages.MSG_LOG_HEALTH_EVALUATION_FAILED);
        }
    }
}
