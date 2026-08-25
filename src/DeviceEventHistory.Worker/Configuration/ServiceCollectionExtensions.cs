using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.Metadata;
using DeviceEventHistory.Infrastructure.MongoDb;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Infrastructure.MongoDb.Stores;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;
using DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeviceEventHistoryConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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

        services.AddSingleton<ConfigurationRedactor>();
        services.AddSingleton<ConfigurationDeviceMetadataResolver>(serviceProvider =>
            new ConfigurationDeviceMetadataResolver(
                serviceProvider.GetRequiredService<IOptions<RfidRawLogOptions>>().Value));
        services.AddSingleton<IDeviceMetadataResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<ConfigurationDeviceMetadataResolver>());

        services.AddSingleton(TimeProvider.System);
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

        services.AddSingleton<BlockTokenizer>();
        services.AddSingleton<IRfidRawRecordParser, RfidRawRecordParser>();
        services.AddSingleton<IRawRecordCanonicalMapper, CanonicalDeviceEventMapper>();
        services.AddSingleton<IProcessRawFileRecordHandler, ProcessRawFileRecordHandler>();

        services.AddSingleton<MongoDbContext>(serviceProvider =>
            new MongoDbContext(
                serviceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value));
        services.AddSingleton<MongoRetryPolicy>(serviceProvider =>
            new MongoRetryPolicy(
                serviceProvider.GetRequiredService<IOptions<IngestionOptions>>().Value.PersistenceRetryCount));
        services.AddSingleton<MongoIndexInitializer>();
        services.AddSingleton<IDeviceEventHistoryWriter, MongoDeviceEventHistoryWriter>();
        services.AddSingleton<IIngestionFailureWriter, MongoIngestionFailureWriter>();
        services.AddSingleton<IIngestionCheckpointStore, MongoIngestionCheckpointStore>();
        services.AddSingleton<IRawRecordPersistenceCoordinator, RawRecordPersistenceCoordinator>();

        return services;
    }
}
