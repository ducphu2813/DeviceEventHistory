using DeviceEventHistory.Application.AppHub.Mapping;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.MongoDb;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Infrastructure.MongoDb.Stores;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Worker.Orchestration;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit.Sdk;

namespace DeviceEventHistory.IntegrationTests;

[Trait("Category", "LiveE2E")]
public sealed class AppHubRealSignalRLiveIntegrationTests
{
    private const string DefaultEndpoint = "http://192.168.1.38:8089/signalr";

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
            AccessToken = IsJwt(token) ? string.Empty : token,
            TokenJwt = IsJwt(token) ? token : string.Empty,
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

        var deviceToken = Environment.GetEnvironmentVariable(
            "DEVICE_EVENT_HISTORY_APPHUB_DEVICE_TOKEN");
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            throw SkipException.ForSkip(
                "Set DEVICE_EVENT_HISTORY_APPHUB_DEVICE_TOKEN to simulate a Device SignalR client.");
        }

        var endpoint = NormalizeSignalREndpoint(
            Environment.GetEnvironmentVariable("DEVICE_EVENT_HISTORY_APPHUB_ENDPOINT")
            ?? DefaultEndpoint);
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
                AccessToken = IsJwt(token) ? string.Empty : token,
                TokenJwt = IsJwt(token) ? token : string.Empty,
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
            var historyColl = mongoContext.GetCollection(AppConst.MongoDb.HistoryCollection);
            var baselineHistoryCount = await historyColl.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Empty);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var runTask = runtime.RunAsync(cts.Token);

            await WaitUntilAsync(
                () => healthState.Snapshot.AvailableSourceCount > 0,
                TimeSpan.FromSeconds(10));

            // Scanner thật phát receiveDeviceScanConnect trong lifecycle kết nối.
            using var deviceConnection = await ConnectDeviceClientAsync(
                endpoint,
                deviceToken,
                deviceId: 999,
                gateId: 1);

            try
            {
                while (await historyColl.CountDocumentsAsync(
                           Builders<BsonDocument>.Filter.Empty) <= baselineHistoryCount)
                {
                    await Task.Delay(100, cts.Token);
                }

                cts.Cancel();
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown via cancellation token
            }

            var historyCount = await historyColl.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

            Assert.True(
                historyCount > baselineHistoryCount,
                "AppHub phải phát event mới và Canonical Ingestion phải lưu event vào MongoDB!");
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

    private static async Task<HubConnection> ConnectDeviceClientAsync(
        string endpoint,
        string token,
        int deviceId,
        int gateId)
    {
        using var connection = new HubConnection(
            endpoint,
            new Dictionary<string, string>
            {
                [IsJwt(token) ? "tokenjwt" : "token"] = token,
                ["sessionType"] = "0",
                ["DeviceId"] = deviceId.ToString(),
                ["GateId"] = gateId.ToString()
            });

        await connection.Start().WaitAsync(TimeSpan.FromSeconds(10));
        return connection;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for AppHub receiver connection.");
            }

            await Task.Delay(100);
        }
    }

    private static string NormalizeSignalREndpoint(string endpoint) =>
        endpoint.TrimEnd('/').EndsWith("/signalr", StringComparison.OrdinalIgnoreCase)
            ? endpoint.TrimEnd('/')
            : $"{endpoint.TrimEnd('/')}/signalr";

    private static bool IsJwt(string token) =>
        token.Count(character => character == '.') == 2;

}
