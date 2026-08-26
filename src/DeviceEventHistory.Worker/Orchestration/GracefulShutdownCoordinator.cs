using DeviceEventHistory.Domain.Common;
using Microsoft.Extensions.Logging;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class GracefulShutdownCoordinator(
    ILogger<GracefulShutdownCoordinator> logger)
{
    public async Task RunAsync(
        Func<CancellationToken, Task> pollingLoop,
        Func<CancellationToken, Task> schedulingLoop,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(pollingLoop);
        ArgumentNullException.ThrowIfNull(schedulingLoop);

        try
        {
            // Start the scheduler first so its consumers are ready before the
            // polling loop begins filling the bounded work queue.
            var schedulingTask = schedulingLoop(stoppingToken);
            var pollingTask = pollingLoop(stoppingToken);

            await Task.WhenAll(
                schedulingTask,
                pollingTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // HostOptions.ShutdownTimeout bounds the host-level graceful shutdown window.
        }
        finally
        {
            logger.LogInformation(AppConst.Logging.IngestionStoppedMessage);
        }
    }
}
