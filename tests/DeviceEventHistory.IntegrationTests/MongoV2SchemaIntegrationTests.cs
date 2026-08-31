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

public sealed class MongoV2SchemaIntegrationTests
{
    [Fact]
    public async Task Initializes_mixed_schema_safely_and_enforces_v2_shape()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            AppConst.EnvironmentVariables.MongoDbConnectionString);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new MongoDbOptions
        {
            ConnectionString = connectionString,
            DatabaseName = $"device_event_history_v2_test_{Guid.NewGuid():N}"
        };
        var context = new MongoDbContext(options);
        var retryPolicy = new MongoRetryPolicy(0);
        var initializer = new MongoIndexInitializer(context, retryPolicy);
        var writer = new MongoDeviceEventHistoryWriter(context, retryPolicy);

        try
        {
            await initializer.InitializeAsync(CancellationToken.None);
            await writer.WriteAsync(
                CreateV2Event(),
                DateTimeOffset.UtcNow,
                "schema-v2-test-worker",
                CancellationToken.None);

            // Re-running against an existing V2 document and the initialized collections
            // must be safe, just as it is when V1 documents already exist.
            await initializer.InitializeAsync(CancellationToken.None);

            var history = context.GetCollection(AppConst.MongoDb.HistoryCollection);
            var document = await history
                .Find(new BsonDocument("eventId", "schema-v2-event"))
                .SingleAsync();

            Assert.Equal(BsonType.String, document["eventId"].BsonType);
            Assert.Equal(BsonType.Int32, document["schemaVersion"].BsonType);
            Assert.Equal(BsonType.Int32, document["companyId"].BsonType);
            Assert.Equal(BsonType.DateTime, document["timelineAtUtc"].BsonType);
            Assert.Equal(BsonType.Array, document["rawPayload"]["arguments"].BsonType);
            Assert.True(document["facts"].AsBsonDocument.Contains("deviceOnline"));
            Assert.False(document["facts"].AsBsonDocument.Contains("connection"));
            Assert.False(document["source"].AsBsonDocument.Contains("fileId"));

            var indexDocuments = await (await history.Indexes.ListAsync()).ToListAsync();
            var indexNames = indexDocuments
                .Select(index => index["name"].AsString)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains(AppConst.MongoDb.HistoryCompanyTimelineIndexName, indexNames);
            Assert.Contains(AppConst.MongoDb.HistoryCompanyCategoryTimelineIndexName, indexNames);
            Assert.Contains(AppConst.MongoDb.HistorySourceReceivedAtIndexName, indexNames);
            Assert.Contains(AppConst.MongoDb.HistoryDeviceTimelineIndexName, indexNames);

            var invalidV2Document = new BsonDocument
            {
                { "eventId", "invalid-v2-event" },
                { "schemaVersion", AppConst.SchemaVersions.CanonicalV2 }
            };
            await Assert.ThrowsAnyAsync<MongoException>(() =>
                history.InsertOneAsync(invalidV2Document));
        }
        finally
        {
            await context.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("dropDatabase", 1),
                cancellationToken: CancellationToken.None);
        }
    }

    private static CanonicalDeviceEvent CreateV2Event() => new()
    {
        EventId = "schema-v2-event",
        SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
        Category = AppConst.Categories.DeviceOnline,
        SourceKind = AppConst.SourceKinds.ErpAppHub,
        CompanyId = 2,
        ReceivedAtUtc = new DateTimeOffset(2026, 8, 31, 8, 30, 0, TimeSpan.Zero),
        TimelineAtUtc = new DateTimeOffset(2026, 8, 31, 8, 30, 0, TimeSpan.Zero),
        TimeBasis = AppConst.TimeBases.Received,
        Source = new CanonicalDeviceEvent.SourceContext
        {
            Producer = AppConst.AppHub.Producer,
            SourceId = "schema-v2-source",
            Transport = AppConst.SourceTransports.ClassicSignalR,
            EventName = AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            DeliveryKind = AppConst.DeliveryKinds.Realtime,
            ConnectionGeneration = "generation-1",
            ReceiveSequence = 1
        },
        Device = new CanonicalDeviceEvent.DeviceContext
        {
            Id = 101,
            GateId = 5,
            Type = AppConst.CanonicalValues.ScannerDeviceType
        },
        RawPayload = new CanonicalDeviceEvent.RawPayloadContext
        {
            Format = AppConst.AppHub.PayloadFormat,
            ArgumentsJson = "[{\"DeviceId\":101,\"Online\":true}]",
            Sha256 = "schema-v2-payload-hash",
            SizeBytes = 32
        },
        Facts = new CanonicalDeviceEvent.FactsContext
        {
            DeviceOnline = new CanonicalDeviceEvent.DeviceOnlineFacts
            {
                Online = true,
                IsSnapshot = false
            }
        },
        Parse = new CanonicalDeviceEvent.ParseContext
        {
            Status = AppConst.Parsing.StatusParsed,
            ParserVersion = AppConst.AppHub.ParserVersion
        }
    };
}
