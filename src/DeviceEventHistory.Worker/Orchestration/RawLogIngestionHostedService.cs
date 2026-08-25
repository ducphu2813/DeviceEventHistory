using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class RawLogIngestionHostedService(
    SourcePollingCoordinator pollingCoordinator,
    FairFileScheduler scheduler,
    GracefulShutdownCoordinator shutdownCoordinator,
    IOptions<WorkerOptions> workerOptions,
    ILogger<RawLogIngestionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            logger.LogInformation(AppConst.Logging.WorkerDisabledMessage);
            return;
        }

        logger.LogInformation(AppConst.Logging.IngestionStartedMessage);
        await shutdownCoordinator.RunAsync(
            pollingCoordinator.RunAsync,
            scheduler.RunAsync,
            stoppingToken);
    }
}
