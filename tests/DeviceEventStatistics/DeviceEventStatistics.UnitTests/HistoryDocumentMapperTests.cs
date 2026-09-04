using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Infrastructure.MongoDb.Mapping;
using MongoDB.Bson;

namespace DeviceEventStatistics.UnitTests;

public sealed class HistoryDocumentMapperTests
{
    private readonly HistoryDocumentMapper mapper = new();

    [Fact]
    public void Maps_v1_raw_log_with_minimal_canonical_fields()
    {
        var document = CreateBaseDocument("a1".PadLeft(64, '0'), 1, "tag_read", "rfid_antenna_file");
        document["source"] = new BsonDocument
        {
            { "sourceId", "antenna-site-a" },
            { "eventName", "raw_record" },
            { "deliveryKind", "activity" }
        };
        document["device"] = new BsonDocument
        {
            { "id", new BsonInt64(101) },
            { "gateId", new BsonInt32(5) },
            { "name", "Gate device" }
        };
        document["facts"] = new BsonDocument
        {
            { "tagRead", new BsonDocument
                {
                    { "tagId", "TAG-1" },
                    { "routingFileId", new BsonInt32(42) }
                } }
        };

        var result = mapper.Map(document);

        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal(101, result.DeviceId);
        Assert.Equal("TAG-1", result.Facts.TagRead?.TagId);
        Assert.Equal(42, result.Facts.TagRead?.RoutingFileId);
        Assert.DoesNotContain(result.MappingDiagnostics, diagnostic =>
            diagnostic.StartsWith("STAT_FIELD_TYPE", StringComparison.Ordinal));
    }

    [Fact]
    public void Maps_v2_apphub_numeric_variants_and_preserves_warning_timeline()
    {
        var document = CreateBaseDocument("b2".PadLeft(64, '0'), 2, "device_connection", "erp_apphub");
        document["timeBasis"] = "received";
        document["source"] = new BsonDocument
        {
            { "sourceId", "erp-apphub-ua" },
            { "eventName", "receiveStateConnected" }
        };
        document["device"] = new BsonDocument("id", "101");
        document["facts"] = new BsonDocument("connection", new BsonDocument
        {
            { "status", "connected" },
            { "isConnected", new BsonInt32(1) }
        });

        var result = mapper.Map(document);

        Assert.Equal(2, result.SchemaVersion);
        Assert.Equal(101, result.DeviceId);
        Assert.Equal("received", result.TimeBasis);
        Assert.Equal("connected", result.Facts.Connection?.Status);
        Assert.Contains("STAT_FIELD_TYPE:facts.connection.isConnected", result.MappingDiagnostics);
    }

    [Fact]
    public void Invalid_event_id_is_kept_as_diagnostic_without_throwing()
    {
        var document = CreateBaseDocument("not-a-sha", 2, "device_online", "erp_apphub");

        var result = mapper.Map(document);

        Assert.Null(result.EventId);
        Assert.Contains("STAT_EVENT_ID_INVALID", result.MappingDiagnostics);
    }

    private static BsonDocument CreateBaseDocument(
        string eventId,
        int schemaVersion,
        string category,
        string sourceKind)
    {
        var timestamp = new BsonDateTime(new DateTime(2026, 8, 28, 8, 30, 0, DateTimeKind.Utc));
        return new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "eventId", eventId },
            { "schemaVersion", schemaVersion },
            { "category", category },
            { "sourceKind", sourceKind },
            { "companyId", new BsonInt32(2) },
            { "receivedAtUtc", timestamp },
            { "persistedAtUtc", timestamp },
            { "timelineAtUtc", timestamp },
            { "timeBasis", "occurred" },
            { "source", new BsonDocument("sourceId", "source-1") },
            { "parse", new BsonDocument("status", "parsed_with_warnings") },
            { "facts", new BsonDocument() }
        };
    }
}
