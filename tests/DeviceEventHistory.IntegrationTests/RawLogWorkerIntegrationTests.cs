using System.Text;
using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.MongoDb;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Infrastructure.MongoDb.Stores;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;
using DeviceEventHistory.Worker.Configuration;
using DeviceEventHistory.Worker.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace DeviceEventHistory.IntegrationTests;

public sealed class RawLogWorkerIntegrationTests
{
    [Fact]
    public async Task Local_raw_file_flows_through_orchestration_to_history_and_checkpoint()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            AppConst.EnvironmentVariables.MongoDbConnectionString);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "device-event-history-integration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourceDate = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        var dateFolder = Path.Combine(
            root,
            sourceDate.Year.ToString("D4"),
            sourceDate.Month.ToString("D2"),
            sourceDate.Day.ToString("D2"));
        Directory.CreateDirectory(dateFolder);

        var rawRecord =
            $"@(TAG001,08:00:00,101,5)b(0)t(1,{sourceDate:dd/MM/yyyy} 08:00:00,{sourceDate:dd/MM/yyyy} 08:00:01,1,20,0,0,920,-55)e(0)\r\n";
        var filePath = Path.Combine(dateFolder, "File_1.txt");
        await File.WriteAllTextAsync(filePath, rawRecord, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var source = new AntennaSourceOptions
        {
            SourceId = "integration-orchestration-source",
            Mode = RawLogSourceMode.Local,
            RootPath = root,
            CompanyId = 2,
            TimeZoneId = "UTC",
            FilePattern = AppConst.RawLog.DefaultFilePattern,
            Enabled = true
        };
        var rawOptions = Options.Create(new RfidRawLogOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(20),
            ReadBufferBytes = 1024,
            MaxRecordBytes = 4096,
            MaxBytesPerTurn = 4096,
            MaxRecordsPerTurn = 100,
            MaxTurnDuration = TimeSpan.FromSeconds(1),
            StartupExistingFilePolicy = FileStartPositionPolicy.Beginning,
            NewFilePolicy = FileStartPositionPolicy.Beginning,
            Sources = [source]
        });
        var workerOptions = Options.Create(new WorkerOptions
        {
            Enabled = true,
            WorkerId = "integration-orchestration-worker"
        });
        var databaseName = $"device_event_history_test_{Guid.NewGuid():N}";
        var mongoOptions = new MongoDbOptions
        {
            ConnectionString = connectionString,
            DatabaseName = databaseName
        };
        var context = new MongoDbContext(mongoOptions);
        var retryPolicy = new MongoRetryPolicy(0);
        var checkpointStore = new MongoIngestionCheckpointStore(context, retryPolicy);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await new MongoIndexInitializer(context, retryPolicy)
                .InitializeAsync(cancellationSource.Token);

            var discovery = new RawLogFileDiscovery(
                rawOptions.Value,
                [new LocalRawLogFileDiscovery()],
                TimeProvider.System);
            var tailReader = new RawLogTailReader(
                rawOptions.Value.ReadBufferBytes,
                [new LocalRawLogTailReader()]);
            var handler = new ProcessRawFileRecordHandler(
                new RfidRawRecordParser(new BlockTokenizer()),
                new CanonicalDeviceEventMapper());
            var coordinator = new RawRecordPersistenceCoordinator(
                new CanonicalIngestionPersistenceService(
                    new MongoDeviceEventHistoryWriter(context, retryPolicy),
                    new MongoIngestionFailureWriter(context, retryPolicy),
                    TimeProvider.System),
                checkpointStore,
                TimeProvider.System);
            var registry = new FileRegistry(
                checkpointStore,
                tailReader,
                () => new RawLogRecordFramer(rawOptions.Value.MaxRecordBytes),
                rawOptions,
                TimeProvider.System);
            var processor = new FileTurnProcessor(
                tailReader,
                handler,
                coordinator,
                checkpointStore,
                rawOptions,
                workerOptions,
                TimeProvider.System);
            var scheduler = new FairFileScheduler(
                maxConcurrentFiles: 1,
                queueCapacity: 4,
                processor,
                NullLogger<FairFileScheduler>.Instance);
            var polling = new SourcePollingCoordinator(
                discovery,
                registry,
                scheduler,
                rawOptions,
                NullLogger<SourcePollingCoordinator>.Instance);

            var schedulerTask = scheduler.RunAsync(cancellationSource.Token);
            var pollingTask = polling.RunAsync(cancellationSource.Token);
            var history = context.GetCollection(AppConst.MongoDb.HistoryCollection);
            var descriptor = RawLogFileDescriptor.TryCreate(
                source,
                sourceDate,
                "File_1.txt",
                filePath,
                new FileInfo(filePath).Length,
                out var fileDescriptor);
            Assert.True(descriptor);
            var key = new IngestionCheckpointKey
            {
                SourceId = source.SourceId,
                FolderDate = sourceDate,
                FileId = 1,
                RelativePath = fileDescriptor!.RelativePath
            };

            await WaitForDocumentAsync(
                history,
                new BsonDocument("source.sourceId", source.SourceId),
                cancellationSource.Token);
            var expectedPosition = new FileInfo(filePath).Length;
            var checkpoint = await WaitForCheckpointAsync(
                checkpointStore,
                key,
                expectedPosition,
                cancellationSource.Token);

            cancellationSource.Cancel();
            await IgnoreCancellationAsync(schedulerTask, pollingTask);

            Assert.Equal(1, await history.CountDocumentsAsync(new BsonDocument("source.sourceId", source.SourceId)));
            Assert.Equal(expectedPosition, checkpoint.Position);
        }
        finally
        {
            cancellationSource.Cancel();
            await context.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("dropDatabase", 1),
                cancellationToken: CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitForDocumentAsync(
        MongoDB.Driver.IMongoCollection<BsonDocument> collection,
        BsonDocument filter,
        CancellationToken cancellationToken)
    {
        while (await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private static async Task<IngestionCheckpoint> WaitForCheckpointAsync(
        IIngestionCheckpointStore checkpointStore,
        IngestionCheckpointKey key,
        long expectedPosition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var checkpoint = await checkpointStore.LoadAsync(key, cancellationToken);
            if (checkpoint is not null && checkpoint.Position == expectedPosition)
            {
                return checkpoint;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private static async Task IgnoreCancellationAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Expected when the integration test stops the polling loop.
        }
    }
}
