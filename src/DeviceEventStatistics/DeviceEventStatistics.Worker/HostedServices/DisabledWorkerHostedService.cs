using Microsoft.Extensions.Options;
using DeviceEventStatistics.Worker.Configuration;

using DeviceEventStatistics.Domain.Common;

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
            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_HOST_STOPPING_DISABLED);
            applicationLifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
