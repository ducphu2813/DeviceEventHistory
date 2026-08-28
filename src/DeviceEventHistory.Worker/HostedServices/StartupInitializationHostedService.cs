using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.HostedServices;

public sealed class StartupInitializationHostedService(
    MongoIndexInitializer indexInitializer,
    IOptions<WorkerOptions> workerOptions,
    IOptions<RfidRawLogOptions> rawLogOptions,
    IOptions<AppHubOptions> appHubOptions,
    IngestionHealthState healthState,
    ILogger<StartupInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            return;
        }

        healthState.ConfigureSources(
            rawLogOptions.Value.Sources
                .Where(source => source.Enabled)
                .Select(source => source.SourceId));
        if (appHubOptions.Value.Enabled)
        {
            healthState.ConfigureSources(
                (appHubOptions.Value.Sources ?? [])
                    .Select(source => source.SourceId));
        }
        await indexInitializer.InitializeAsync(cancellationToken);
        healthState.MarkMongoAvailable();
        healthState.MarkStartupReady();
        logger.LogInformation(AppConst.Logging.MongoIndexesInitializedMessage);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
