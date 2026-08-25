using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.HostedServices;

public sealed class StartupInitializationHostedService(
    MongoIndexInitializer indexInitializer,
    IOptions<WorkerOptions> workerOptions,
    ILogger<StartupInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            return;
        }

        await indexInitializer.InitializeAsync(cancellationToken);
        logger.LogInformation(AppConst.Logging.MongoIndexesInitializedMessage);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
