using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker;

public class Worker(ILogger<Worker> logger, IOptions<WorkerOptions> workerOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            logger.LogInformation("Device Event History Worker is disabled by configuration.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        logger.LogWarning("Worker is enabled, but the raw-log ingestion pipeline is not implemented in this work package.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}
