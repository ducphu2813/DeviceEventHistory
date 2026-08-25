using DeviceEventHistory.Worker.Configuration;
using DeviceEventHistory.Domain.Common;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker;

public class Worker(ILogger<Worker> logger, IOptions<WorkerOptions> workerOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            logger.LogInformation(AppConst.Logging.WorkerDisabledMessage);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        logger.LogWarning(AppConst.Logging.IngestionNotImplementedMessage);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
