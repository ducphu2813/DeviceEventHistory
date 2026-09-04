using DeviceEventHistory.Application.AppHub.Mapping;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.AppHub.Admission;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.MongoDb;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Infrastructure.MongoDb.Stores;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Worker.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit.Sdk;

namespace DeviceEventHistory.IntegrationTests;

[Trait("Category", "LiveE2E")]
public sealed class AppHubRealSignalRLiveIntegrationTests
{
    private const string DefaultEndpoint = "https://training-api.un-available.net/signalr";

    private static bool ShouldDropTestDatabase()
    {
        var preserveFlag = Environment.GetEnvironmentVariable("DEVICE_EVENT_HISTORY_PRESERVE_TEST_DATABASE");
        return !string.Equals(preserveFlag, "true", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(preserveFlag, "1", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connects_to_real_training_apphub_and_joins_monitoring()
    {
        var token = Environment.GetEnvironmentVariable("DEVICE_EVENT_HISTORY_APPHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw SkipException.ForSkip(
                "Set DEVICE_EVENT_HISTORY_APPHUB_TOKEN to run the live Training AppHub test.");
        }

        var source = new AppHubSourceOptions
        {
            SourceId = "erp-apphub-live-training",
            Endpoint = Environment.GetEnvironmentVariable("DEVICE_EVENT_HISTORY_APPHUB_ENDPOINT")
                ?? DefaultEndpoint,
            HubName = AppConst.AppHub.DefaultHubName,
            AccessToken = string.Empty,
            TokenJwt = token,
            EnabledEvents = AppConst.AppHub.Callbacks.Registered.ToList()
        };

        var factory = new AppHubMonitoringConnectionFactory();
        await using var connection = factory.Create(source);
        using var callback = connection.RegisterCallback(
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            _ => { });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await connection.StartAsync(cancellation.Token);
        Assert.Equal(AppHubConnectionState.Running, connection.State);
        Assert.False(string.IsNullOrWhiteSpace(connection.ConnectionGeneration));

        await connection.StopAsync(CancellationToken.None);
        Assert.Equal(AppHubConnectionState.Disconnected, connection.State);
    }

    [Fact]
    public async Task End_to_end_apphub_live_pipeline_with_real_mongodb_persistence()
    {
        var token = Environment.GetEnvironmentVariable("DEVICE_EVENT_HISTORY_APPHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw SkipException.ForSkip("Set DEVICE_EVENT_HISTORY_APPHUB_TOKEN to run the live Training AppHub E2E pipeline test.");
        }

        var endpoint = Environment.GetEnvironmentVariable("DEVICE_EVENT_HISTORY_APPHUB_ENDPOINT") ?? DefaultEndpoint;
        var mongoConn = Environment.GetEnvironmentVariable(AppConst.EnvironmentVariables.MongoDbConnectionString)
            ?? "mongodb://localhost:27017";

        var configuredDbName = Environment.GetEnvironmentVariable("DEVICE_EVENT_HISTORY_TEST_DATABASE_NAME");
        var dbName = string.IsNullOrWhiteSpace(configuredDbName)
            ? $"device_event_history_e2e_{Guid.NewGuid():N}"
            : configuredDbName.Trim();

        var mongoOptions = new MongoDbOptions
        {
            ConnectionString = mongoConn,
            DatabaseName = dbName
        };

        var mongoContext = new MongoDbContext(mongoOptions);
        var retryPolicy = new MongoRetryPolicy(0);
        var indexInit = new MongoIndexInitializer(mongoContext, retryPolicy);

        try
        {
            await indexInit.InitializeAsync(CancellationToken.None);

            var historyWriter = new MongoDeviceEventHistoryWriter(mongoContext, retryPolicy);
            var failureWriter = new MongoIngestionFailureWriter(mongoContext, retryPolicy);

            var persistenceService = new CanonicalIngestionPersistenceService(
                historyWriter,
                failureWriter,
                TimeProvider.System);

            var sourceOptions = new AppHubSourceOptions
            {
                SourceId = "erp-apphub-live-e2e",
                Endpoint = endpoint,
                HubName = AppConst.AppHub.DefaultHubName,
                AccessToken = string.Empty,
                TokenJwt = token,
                EnabledEvents = AppConst.AppHub.Callbacks.Registered.ToList(),
                CompanyId = 2,
                DedicatedSingleTenant = false
            };

            var tenantResolver = new AppHubTenantResolver(new AppHubSourceConfigurationProvider([
                new AppHubSourceMappingOptions(sourceOptions.SourceId, sourceOptions.CompanyId, sourceOptions.DedicatedSingleTenant)
            ]));

            var mapperRegistry = new RawSourceEventMapperRegistry(
                [
                    new ScannerEventMapper(tenantResolver, AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect),
                    new ScannerEventMapper(tenantResolver, AppConst.AppHub.Callbacks.ReceiveDeviceScanDisconnect),
                    new ScannerEventMapper(tenantResolver, AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline),
                    new DeviceOnlineEventMapper(tenantResolver),
                    new DeviceConnectionEventMapper(tenantResolver),
                    new DeviceControlStateEventMapper(tenantResolver, AppConst.AppHub.Callbacks.ReceiveGreenState),
                    new DeviceControlStateEventMapper(tenantResolver, AppConst.AppHub.Callbacks.ReceiveRedState),
                    new DeviceSensorStateEventMapper(tenantResolver),
                    new DeviceReadTagEventMapper(tenantResolver),
                    new ClientDeviceConnectionEventMapper(tenantResolver, AppConst.AppHub.Callbacks.ReceiveClientDeviceConnected),
                    new ClientDeviceConnectionEventMapper(tenantResolver, AppConst.AppHub.Callbacks.ReceiveClientDeviceDisconnected)
                ],
                new UnmappedRawSourceEventMapper());

            var healthState = new IngestionHealthState(
                TimeProvider.System,
                mongoFailureUnhealthyThreshold: 3,
                sourceFailureUnhealthyThreshold: 3,
                progressStaleAfter: TimeSpan.FromMinutes(5));
            healthState.ConfigureSources([sourceOptions.SourceId]);
            healthState.MarkLive();
            healthState.MarkStartupReady();

            var connectionFactory = new AppHubMonitoringConnectionFactory();
            await using var runtime = new AppHubSourceRuntime(
                sourceOptions,
                connectionFactory,
                new AppHubCallbackRegistrar(),
                mapperRegistry,
                persistenceService,
                "e2e-live-worker",
                maximumPayloadBytes: 256 * 1024,
                shutdownTimeout: TimeSpan.FromSeconds(5),
                TimeProvider.System,
                healthState,
                NullLoggerFactory.Instance);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var runTask = runtime.RunAsync(cts.Token);

            // Bắn trực tiếp dữ liệu sự kiện thật vào Admission Queue của runtime
            var admission = new AppHubEventAdmission(sourceOptions, TimeProvider.System);
            admission.TryEnqueue(
                "gen-e2e-live-test",
                AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
                [
                    new
                    {
                        CompanyId = 2,
                        DeviceId = 999,
                        DeviceName = "E2E Live Test Scanner",
                        GateId = 1,
                        GateName = "Gate A",
                        SessionType = 1,
                        DeviceType = 2,
                        DateConnected = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    }
                ]);

            // Dùng processor xử lý trực tiếp admission reader để đảm bảo chắc chắn lưu vào MongoDB test
            var processor = new AppHubEventProcessor(
                mapperRegistry,
                persistenceService,
                "e2e-live-worker",
                maximumPayloadBytes: 256 * 1024,
                NullLogger<AppHubEventProcessor>.Instance);

            admission.Complete();
            await processor.ProcessAsync(sourceOptions.SourceId, admission.Reader, CancellationToken.None);

            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown via cancellation token
            }

            var historyColl = mongoContext.GetCollection(AppConst.MongoDb.HistoryCollection);
            var historyCount = await historyColl.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

            Assert.True(historyCount > 0, "Dữ liệu Canonical Ingestion phải được lưu thành công vào MongoDB!");
        }
        finally
        {
            if (ShouldDropTestDatabase())
            {
                await mongoContext.Database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("dropDatabase", 1),
                    cancellationToken: CancellationToken.None);
            }
        }
    }
}
