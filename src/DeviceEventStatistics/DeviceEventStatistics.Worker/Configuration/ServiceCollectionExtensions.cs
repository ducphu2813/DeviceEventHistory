using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Infrastructure.MongoDb;
using DeviceEventStatistics.Infrastructure.SqlServer;
using DeviceEventStatistics.Infrastructure.SqlServer.Execution;
using DeviceEventStatistics.Infrastructure.SqlServer.Mapping;
using DeviceEventStatistics.Infrastructure.SqlServer.Schema;
using DeviceEventStatistics.Infrastructure.SqlServer.Stores;
using DeviceEventStatistics.Infrastructure.Metadata;
using DeviceEventStatistics.Infrastructure.MongoDb.Indexes;
using DeviceEventStatistics.Infrastructure.MongoDb.Mapping;
using DeviceEventStatistics.Infrastructure.MongoDb.Reading;
using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Metadata;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Domain.State;
using DeviceEventStatistics.Worker.HealthChecks;
using DeviceEventStatistics.Worker.Orchestration;
using DeviceEventStatistics.Infrastructure.Observability;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace DeviceEventStatistics.Worker.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeviceEventStatisticsConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = configuration
                .GetSection(WorkerOptions.SectionName)
                .GetValue(
                    nameof(WorkerOptions.ShutdownTimeout),
                    TimeSpan.FromSeconds(30));
        });

        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();

        services.AddOptions<ProjectionOptions>()
            .Bind(configuration.GetSection(ProjectionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ProjectionOptions>, ProjectionOptionsValidator>();

        services.AddOptions<StateOptions>()
            .Bind(configuration.GetSection(StateOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StateOptions>, StateOptionsValidator>();

        services.AddOptions<ReconciliationOptions>()
            .Bind(configuration.GetSection(ReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReconciliationOptions>, ReconciliationOptionsValidator>();

        services.AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection(RetentionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RetentionOptions>, RetentionOptionsValidator>();

        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ObservabilityOptions>, ObservabilityOptionsValidator>();

        services.AddOptions<MetadataOptions>()
            .Bind(configuration.GetSection(MetadataOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MetadataOptions>, MetadataOptionsValidator>();

        services.AddOptions<DatabaseSettingsOptions>()
            .Bind(configuration.GetSection(DatabaseSettingsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IConfigureOptions<DatabaseSettingsOptions>, DatabaseSettingsOptionsRegistration>();
        services.AddSingleton<IValidateOptions<DatabaseSettingsOptions>, DatabaseSettingsOptionsValidator>();

        services.AddSingleton<ConfigurationRedactor>();
        services.AddSingleton<StartupReadinessBarrier>();
        services.AddSingleton<StartupReadinessState>();
        services.AddSingleton<ProjectionDefinitionRuntimeState>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    public static IServiceCollection AddDeviceEventStatisticsInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<IMongoClient>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<DatabaseSettingsOptions>>()
                .Value.MongoDb;
            return new MongoClient(options.ConnectionString);
        });

        services.AddSingleton<MongoHistoryDbContext>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<DatabaseSettingsOptions>>()
                .Value.MongoDb;
            return new MongoHistoryDbContext(
                options,
                serviceProvider.GetRequiredService<IMongoClient>());
        });
        services.AddSingleton<HistoryDocumentMapper>();
        services.AddSingleton<IHistoryEventReader, MongoHistoryEventReader>();
        services.AddSingleton<IHistoryRangeReader, MongoHistoryRangeReader>();
        services.AddSingleton<IHistoryContractAuditReader, MongoHistoryContractAuditReader>();
        services.AddSingleton<MongoHistoryIndexVerifier>();
        services.AddSingleton<HistoryEventEligibilityPolicy>();
        services.AddSingleton<EventOwnershipPolicy>();
        services.AddSingleton<ProjectionEventOutcomeMapper>();
        services.AddSingleton<LocalStatisticsDateResolver>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MetadataOptions>>().Value;
            return new LocalStatisticsDateResolver(options.TimeZoneId);
        });
        services.AddSingleton<IDeviceMetadataResolver>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MetadataOptions>>().Value;
            return new ConfigurationDeviceMetadataResolver(options.TimeZoneId, options.UtcOffsetMinutes);
        });
        services.AddSingleton<IDeviceMetricMapper, RawFileMetricMapper>();
        services.AddSingleton<IDeviceMetricMapper, AppHubConnectionMetricMapper>();
        services.AddSingleton<IDeviceMetricMapper, AppHubDeviceOnlineMetricMapper>();
        services.AddSingleton<IDeviceMetricMapper, AppHubControlMetricMapper>();
        services.AddSingleton<IDeviceMetricMapper, AppHubSensorMetricMapper>();
        services.AddSingleton<IDeviceMetricMapper, AppHubScannerMetricMapper>();
        services.AddSingleton<DeviceMetricMapperRegistry>();

        services.AddSingleton<SqlStatisticsDbContext>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<DatabaseSettingsOptions>>()
                .Value.SqlServer;
            return new SqlStatisticsDbContext(options);
        });
        services.AddSingleton<SqlSchemaVerifier>();
        services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<DatabaseSettingsOptions>>().Value.SqlServer);
        services.AddSingleton<ProjectionTvpMapper>();
        services.AddSingleton<SqlRetryPolicy>();
        services.AddSingleton<SqlProjectionBatchOperations>();
        services.AddSingleton<SqlDeviceDimensionStore>();
        services.AddSingleton<IDeviceDimensionStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlDeviceDimensionStore>());
        services.AddSingleton<IProjectionDefinitionStore, SqlProjectionDefinitionStore>();
        services.AddSingleton<IProjectionDefinitionResolver, ProjectionDefinitionResolver>();
        services.AddSingleton<IMetricKeyResolver, SqlMetricKeyResolver>();
        services.AddSingleton<IncrementalProjectionHandler>();
        services.AddSingleton<HistoryContractAuditHandler>();
        services.AddSingleton<StatisticsProjectionPipeline>();
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ProjectionOptions>>().Value;
            return new SqlProjectionWriterOptions(
                options.PersistenceRetryCount,
                options.RetryMinDelay,
                options.RetryMaxDelay);
        });
        services.AddSingleton<IStatisticsBatchWriter, SqlStatisticsBatchWriter>();
        services.AddSingleton<IDurationRefreshStore, SqlDurationRefreshStore>();
        services.AddSingleton<ProjectionLeaseCoordinator>();
        services.AddSingleton<GracefulShutdownCoordinator>();
        services.AddSingleton<StateDurationCalculator>();
        services.AddSingleton<IProjectionLeaseStore, SqlProjectionLeaseStore>();
        services.AddSingleton<IProjectionCheckpointStore, SqlProjectionCheckpointStore>();
        services.AddSingleton<SqlProjectionLeaseStore>();
        services.AddSingleton<SqlProjectionCheckpointStore>();
        services.AddSingleton<IProjectionScopeReader, SqlProjectionScopeReader>();
        services.AddSingleton<ProjectionCoveragePolicy>();
        services.AddSingleton<ForwardStatePropagation>();
        services.AddSingleton<IReconciliationRequestStore, SqlReconciliationRequestStore>();
        services.AddSingleton<IProjectionRebuildStore, SqlProjectionRebuildStore>();
        services.AddSingleton<IProjectionRecoveryStore, SqlProjectionRecoveryStore>();
        services.AddSingleton<IOperationalCleanupStore, SqlOperationalCleanupStore>();
        services.AddSingleton<ExactRangeRebuilder>();
        services.AddSingleton<ReconciliationCoordinator>();
        services.AddSingleton<IProjectionOperationalSnapshotReader, SqlProjectionOperationalSnapshotReader>();

        return services;
    }

    public static IServiceCollection AddDeviceEventStatisticsObservability(
        this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<StartupReadinessHealthCheck>(
                "statistics_startup_readiness",
                tags: ["ready", "startup"])
            .AddCheck<OperationalHealthCheck>(
                "statistics_operational_health",
                tags: ["ready", "operational"]);
        services.AddSingleton<StatisticsHealthEvaluator>();
        services.AddSingleton<OperationalHealthState>();
        services.AddSingleton<IStatisticsTelemetry, StatisticsMetrics>();
        return services;
    }
}
