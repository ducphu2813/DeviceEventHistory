using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class ErpAppHubMonitoringHostedService(
    IOptions<WorkerOptions> workerOptions,
    IOptions<AppHubOptions> appHubOptions,
    IOptions<IngestionOptions> ingestionOptions,
    IAppHubMonitoringConnectionFactory connectionFactory,
    AppHubCallbackRegistrar callbackRegistrar,
    RawSourceEventMapperRegistry mapperRegistry,
    ICanonicalIngestionPersistenceService persistenceService,
    TimeProvider timeProvider,
    IngestionHealthState healthState,
    ILoggerFactory loggerFactory,
    IIngestionTelemetry? telemetry = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!workerOptions.Value.Enabled || !appHubOptions.Value.Enabled)
        {
            return;
        }

        var sources = (appHubOptions.Value.Sources ?? []).ToArray();
        if (sources.Length == 0)
        {
            return;
        }

        var runtimes = sources
            .Select(source => new AppHubSourceRuntime(
                source,
                connectionFactory,
                callbackRegistrar,
                mapperRegistry,
                persistenceService,
                workerOptions.Value.WorkerId,
                ingestionOptions.Value.MaxRawPayloadBytes,
                ingestionOptions.Value.ShutdownTimeout,
                timeProvider,
                healthState,
                loggerFactory,
                telemetry))
            .ToArray();

        loggerFactory
            .CreateLogger<ErpAppHubMonitoringHostedService>()
            .LogInformation(
                AppConst.Logging.AppHubIngestionStartedMessage,
                sources.Length);
        try
        {
            await Task.WhenAll(runtimes.Select(runtime => runtime.RunAsync(stoppingToken)));
        }
        finally
        {
            foreach (var runtime in runtimes)
            {
                await runtime.DisposeAsync();
            }

            loggerFactory
                .CreateLogger<ErpAppHubMonitoringHostedService>()
                .LogInformation(AppConst.Logging.AppHubIngestionStoppedMessage);
        }
    }
}
