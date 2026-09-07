using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class GracefulShutdownHostedService(
    GracefulShutdownCoordinator shutdownCoordinator,
    IOptions<WorkerOptions> workerOptions,
    ILogger<GracefulShutdownHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var activeOperations = shutdownCoordinator.ActiveOperations;
        shutdownCoordinator.BeginDrain();
        logger.LogInformation(
            StatisticsContractConstants.Messages.MSG_LOG_SHUTDOWN_DRAIN_STARTED,
            activeOperations);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(workerOptions.Value.ShutdownTimeout);
        try
        {
            await shutdownCoordinator.WaitForDrainAsync(timeout.Token);
            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_SHUTDOWN_DRAIN_COMPLETED,
                shutdownCoordinator.ActiveOperations);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                StatisticsContractConstants.Messages.MSG_LOG_SHUTDOWN_DRAIN_TIMEOUT,
                shutdownCoordinator.ActiveOperations);
        }
    }
}
