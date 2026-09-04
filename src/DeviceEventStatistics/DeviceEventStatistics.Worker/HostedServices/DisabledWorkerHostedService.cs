using Microsoft.Extensions.Options;
using DeviceEventStatistics.Worker.Configuration;

namespace DeviceEventStatistics.Worker.HostedServices;

public sealed class DisabledWorkerHostedService(
    IOptions<WorkerOptions> workerOptions,
    IHostApplicationLifetime applicationLifetime,
    ILogger<DisabledWorkerHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            logger.LogInformation("Statistics worker is disabled; stopping the host without opening a processing loop.");
            applicationLifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
