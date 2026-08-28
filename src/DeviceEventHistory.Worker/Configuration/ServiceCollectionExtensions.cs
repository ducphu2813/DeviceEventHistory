using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.Metadata;
using DeviceEventHistory.Infrastructure.MongoDb;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Infrastructure.MongoDb.Stores;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;
using DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;
using DeviceEventHistory.Worker.Orchestration;
using DeviceEventHistory.Worker.HostedServices;
using DeviceEventHistory.Worker.HealthChecks;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeviceEventHistoryConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = configuration.GetSection(IngestionOptions.SectionName)
                .GetValue(
                    nameof(IngestionOptions.ShutdownTimeout),
                    TimeSpan.FromSeconds(AppConst.Defaults.ShutdownTimeoutSeconds));
        });

        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();

        services.AddOptions<RfidRawLogOptions>()
            .Bind(configuration.GetSection(RfidRawLogOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RfidRawLogOptions>, RfidRawLogOptionsValidator>();

        services.AddOptions<MongoDbOptions>()
            .Bind(configuration.GetSection(MongoDbOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IConfigureOptions<MongoDbOptions>, ConfigurationOptionsRegistration>();
        services.AddSingleton<IValidateOptions<MongoDbOptions>, MongoDbOptionsValidator>();

        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IngestionOptions>, IngestionOptionsValidator>();

        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ObservabilityOptions>, ObservabilityOptionsValidator>();

        services.AddSingleton<ConfigurationRedactor>();
        services.AddSingleton<ConfigurationDeviceMetadataResolver>(serviceProvider =>
            new ConfigurationDeviceMetadataResolver(
                serviceProvider.GetRequiredService<IOptions<RfidRawLogOptions>>().Value));
        services.AddSingleton<IDeviceMetadataResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<ConfigurationDeviceMetadataResolver>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IngestionHealthState>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
            return new IngestionHealthState(
                serviceProvider.GetRequiredService<TimeProvider>(),
                options.MongoFailureUnhealthyThreshold,
                options.SourceFailureUnhealthyThreshold,
                options.ProgressStaleAfter);
        });
        services.AddSingleton<IngestionMetrics>();
        services.AddSingleton<IIngestionTelemetry>(serviceProvider =>
            serviceProvider.GetRequiredService<IngestionMetrics>());
        services.AddSingleton<IRawLogSourceFileDiscovery, LocalRawLogFileDiscovery>();
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(AppConst.Defaults.RemoteRequestTimeoutSeconds)
        });
        services.AddSingleton<RemoteHttpRawLogFileDiscovery>();
        services.AddSingleton<IRawLogSourceFileDiscovery>(serviceProvider =>
            serviceProvider.GetRequiredService<RemoteHttpRawLogFileDiscovery>());
        services.AddSingleton<IRawLogFileDiscovery>(serviceProvider =>
            new RawLogFileDiscovery(
                serviceProvider.GetRequiredService<IOptions<RfidRawLogOptions>>().Value,
                serviceProvider.GetServices<IRawLogSourceFileDiscovery>(),
                serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IRawLogSourceTailReader, LocalRawLogTailReader>();
        services.AddSingleton<RemoteHttpRawLogTailReader>();
        services.AddSingleton<IRawLogSourceTailReader>(serviceProvider =>
            serviceProvider.GetRequiredService<RemoteHttpRawLogTailReader>());
        services.AddSingleton<IRawLogTailReader>(serviceProvider =>
            new RawLogTailReader(
                serviceProvider.GetRequiredService<IOptions<RfidRawLogOptions>>().Value.ReadBufferBytes,
                serviceProvider.GetServices<IRawLogSourceTailReader>()));
        services.AddTransient<IRawLogRecordFramer>(serviceProvider =>
            new RawLogRecordFramer(
                serviceProvider.GetRequiredService<IOptions<RfidRawLogOptions>>().Value.MaxRecordBytes));
        services.AddSingleton<Func<IRawLogRecordFramer>>(serviceProvider =>
            () => serviceProvider.GetRequiredService<IRawLogRecordFramer>());

        services.AddSingleton<BlockTokenizer>();
        services.AddSingleton<IRfidRawRecordParser, RfidRawRecordParser>();
        services.AddSingleton<IRawRecordCanonicalMapper, CanonicalDeviceEventMapper>();
        services.AddSingleton<IProcessRawFileRecordHandler, ProcessRawFileRecordHandler>();
        services.AddSingleton<UnmappedRawSourceEventMapper>();
        services.AddSingleton<RawSourceEventMapperRegistry>(serviceProvider =>
            new RawSourceEventMapperRegistry(
                serviceProvider.GetServices<IRawSourceEventMapper>(),
                serviceProvider.GetRequiredService<UnmappedRawSourceEventMapper>()));

        services.AddSingleton<MongoDbContext>(serviceProvider =>
            new MongoDbContext(
                serviceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value));
        services.AddSingleton<MongoRetryPolicy>(serviceProvider =>
            new MongoRetryPolicy(
                serviceProvider.GetRequiredService<IOptions<IngestionOptions>>().Value.PersistenceRetryCount,
                serviceProvider.GetRequiredService<IIngestionTelemetry>()));
        services.AddSingleton<MongoIndexInitializer>();
        services.AddSingleton<IDeviceEventHistoryWriter, MongoDeviceEventHistoryWriter>();
        services.AddSingleton<IIngestionFailureWriter, MongoIngestionFailureWriter>();
        services.AddSingleton<ICanonicalIngestionPersistenceService, CanonicalIngestionPersistenceService>();
        services.AddSingleton<IIngestionCheckpointStore, MongoIngestionCheckpointStore>();
        services.AddSingleton<IRawRecordPersistenceCoordinator, RawRecordPersistenceCoordinator>();

        services.AddSingleton<FileRegistry>();
        services.AddSingleton<FileTurnProcessor>();
        services.AddSingleton<FairFileScheduler>(serviceProvider =>
        {
            var rawLog = serviceProvider.GetRequiredService<IOptions<RfidRawLogOptions>>().Value;
            var queueCapacity = Math.Max(
                rawLog.MaxConcurrentFiles,
                rawLog.MaxConcurrentFiles * AppConst.Defaults.SchedulerQueueMultiplier);
            return new FairFileScheduler(
                rawLog.MaxConcurrentFiles,
                queueCapacity,
                serviceProvider.GetRequiredService<FileTurnProcessor>(),
                serviceProvider.GetRequiredService<ILogger<FairFileScheduler>>(),
                serviceProvider.GetRequiredService<IIngestionTelemetry>(),
                serviceProvider.GetRequiredService<IOptions<WorkerOptions>>());
        });
        services.AddSingleton<SourcePollingCoordinator>();
        services.AddSingleton<GracefulShutdownCoordinator>();
        services.AddHealthChecks()
            .AddCheck<MongoDbHealthCheck>(AppConst.Observability.MongoHealthCheckName)
            .AddCheck<SourcePathHealthCheck>(AppConst.Observability.SourceHealthCheckName)
            .AddCheck<IngestionProgressHealthCheck>(AppConst.Observability.IngestionHealthCheckName);
        services.AddHostedService<StartupInitializationHostedService>();

        return services;
    }
}
