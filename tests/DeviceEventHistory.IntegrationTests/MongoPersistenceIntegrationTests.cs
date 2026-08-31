using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.MongoDb;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Indexes;
using DeviceEventHistory.Infrastructure.MongoDb.Stores;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventHistory.IntegrationTests;

public sealed class MongoPersistenceIntegrationTests
{
    [Fact]
    public async Task Initializes_indexes_persists_idempotently_and_advances_checkpoint_with_cas()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            AppConst.EnvironmentVariables.MongoDbConnectionString);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // The test is opt-in so a build agent without MongoDB keeps the default suite green.
            return;
        }

        var databaseName = $"device_event_history_test_{Guid.NewGuid():N}";
        var options = new MongoDbOptions
        {
            ConnectionString = connectionString,
            DatabaseName = databaseName
        };
        var context = new MongoDbContext(options);
        var retryPolicy = new MongoRetryPolicy(0);
        var initializer = new MongoIndexInitializer(context, retryPolicy);
        var historyWriter = new MongoDeviceEventHistoryWriter(context, retryPolicy);
        var checkpointStore = new MongoIngestionCheckpointStore(context, retryPolicy);

        try
        {
            await initializer.InitializeAsync(CancellationToken.None);
            await initializer.InitializeAsync(CancellationToken.None);

            var deviceEvent = CreateDeviceEvent();
            var receivedAtUtc = DateTimeOffset.UtcNow;
            var firstWrite = await historyWriter.WriteAsync(
                deviceEvent,
                receivedAtUtc,
                "integration-test-worker",
                CancellationToken.None);
            var duplicateWrite = await historyWriter.WriteAsync(
                deviceEvent,
                receivedAtUtc,
                "integration-test-worker",
                CancellationToken.None);

            Assert.False(firstWrite.WasAlreadyPersisted);
            Assert.True(duplicateWrite.WasAlreadyPersisted);
            Assert.Equal(
                1,
                await context
                    .GetCollection(AppConst.MongoDb.HistoryCollection)
                    .CountDocumentsAsync(new BsonDocument("eventId", deviceEvent.EventId)));

            var key = new IngestionCheckpointKey
            {
                SourceId = deviceEvent.Source.SourceId,
                FolderDate = deviceEvent.Source.FolderDate!.Value,
                FileId = deviceEvent.Source.FileId!.Value,
                RelativePath = deviceEvent.Source.RelativePath!
            };
            var initial = await checkpointStore.LoadAsync(key, CancellationToken.None);
            Assert.Null(initial);

            var advanced = await checkpointStore.AdvanceAsync(
                key,
                expectedVersion: 0,
                new CheckpointAdvanceRequest
                {
                    Position = deviceEvent.Source.OffsetEnd!.Value,
                    LastRecordHash = deviceEvent.RawPayload.Sha256,
                    LastEventId = deviceEvent.EventId,
                    ObservedFileLength = deviceEvent.Source.OffsetEnd!.Value,
                    WorkerId = "integration-test-worker",
                    UpdatedAtUtc = receivedAtUtc
                },
                CancellationToken.None);
            Assert.True(advanced.IsAdvanced);
            Assert.Equal(1, advanced.Checkpoint!.Version);

            var conflict = await checkpointStore.AdvanceAsync(
                key,
                expectedVersion: 0,
                new CheckpointAdvanceRequest
                {
                    Position = deviceEvent.Source.OffsetEnd!.Value + 10,
                    LastRecordHash = deviceEvent.RawPayload.Sha256,
                    LastEventId = deviceEvent.EventId,
                    WorkerId = "another-worker",
                    UpdatedAtUtc = receivedAtUtc
                },
                CancellationToken.None);
            Assert.False(conflict.IsAdvanced);

            var loaded = await checkpointStore.LoadAsync(key, CancellationToken.None);
            Assert.Equal(deviceEvent.Source.OffsetEnd, loaded!.Position);
            Assert.Equal(1, loaded.Version);
        }
        finally
        {
            await context.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("dropDatabase", 1),
                cancellationToken: CancellationToken.None);
        }
    }

    private static CanonicalDeviceEvent CreateDeviceEvent() => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        SchemaVersion = AppConst.RawLog.SchemaVersion,
        Category = AppConst.Categories.TagRead,
        SourceKind = AppConst.RawLog.SourceKind,
        CompanyId = 2,
        OccurredAtUtc = new DateTimeOffset(2026, 8, 25, 7, 0, 0, TimeSpan.Zero),
        OccurredAtLocal = new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.FromHours(7)),
        Source = new CanonicalDeviceEvent.SourceContext
        {
            Producer = AppConst.RawLog.Producer,
            SourceId = "integration-test-source",
            FileId = 12,
            FileName = "File_12.txt",
            RelativePath = "2026/08/25/File_12.txt",
            FolderDate = new DateOnly(2026, 8, 25),
            OffsetStart = 100,
            OffsetEnd = 200
        },
        Device = new CanonicalDeviceEvent.DeviceContext { Id = 101, GateId = 5 },
        RawPayload = new CanonicalDeviceEvent.RawPayloadContext
        {
            Format = AppConst.RawLog.PayloadFormat,
            Text = "@(TAG001,14:00:00,101,5)e(0)",
            Sha256 = "integration-test-hash"
        },
        Facts = new CanonicalDeviceEvent.FactsContext
        {
            TagRead = new CanonicalDeviceEvent.TagReadFacts
            {
                TagId = "TAG001",
                RoutingFileId = 12
            }
        },
        Parse = new CanonicalDeviceEvent.ParseContext
        {
            Status = AppConst.Parsing.StatusParsed,
            ParserVersion = AppConst.RawLog.ParserVersion
        }
    };
}
