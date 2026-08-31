using DeviceEventHistory.Domain.Common;
using MongoDB.Bson;

namespace DeviceEventHistory.Infrastructure.MongoDb.Schema;

internal static class MongoSchemaValidator
{
    public static BsonDocument History() => VersionedValidator(HistoryV2());

    public static BsonDocument Failure() => VersionedValidator(FailureV2());

    public static BsonDocument Checkpoint() => VersionedValidator(CheckpointV2());

    private static BsonDocument VersionedValidator(BsonDocument schema) => new()
    {
        {
            "$or",
            new BsonArray
            {
                new BsonDocument("schemaVersion", new BsonDocument("$ne", AppConst.SchemaVersions.CanonicalV2)),
                new BsonDocument("$jsonSchema", schema)
            }
        }
    };

    private static BsonDocument HistoryV2() => new()
    {
        { "bsonType", "object" },
        {
            "required",
            new BsonArray
            {
                "eventId", "schemaVersion", "category", "sourceKind", "companyId",
                "receivedAtUtc", "persistedAtUtc", "timelineAtUtc", "timeBasis",
                "source", "rawPayload", "facts", "parse", "ingestion"
            }
        },
        {
            "properties",
            new BsonDocument
            {
                { "eventId", StringSchema() },
                { "schemaVersion", IntegerSchema() },
                { "category", StringSchema() },
                { "sourceKind", StringSchema() },
                { "companyId", PositiveIntegerSchema() },
                { "receivedAtUtc", DateSchema() },
                { "persistedAtUtc", DateSchema() },
                { "timelineAtUtc", DateSchema() },
                { "timeBasis", StringSchema() },
                { "source", SourceSchema() },
                { "rawPayload", RawPayloadSchema(allowStringArguments: false) },
                { "facts", ObjectSchema() },
                { "parse", ParseSchema() },
                { "ingestion", IngestionSchema() }
            }
        }
    };

    private static BsonDocument FailureV2() => new()
    {
        { "bsonType", "object" },
        {
            "required",
            new BsonArray
            {
                "failureId", "schemaVersion", "sourceKind", "companyId", "source",
                "rawPayload", "error", "receivedAtUtc", "persistedAtUtc", "retryable",
                "retryCount", "ingestion"
            }
        },
        {
            "properties",
            new BsonDocument
            {
                { "failureId", StringSchema() },
                { "schemaVersion", IntegerSchema() },
                { "sourceKind", StringSchema() },
                { "companyId", new BsonDocument("bsonType", new BsonArray { "int", "long", "null" }) },
                { "source", SourceSchema() },
                { "rawPayload", RawPayloadSchema(allowStringArguments: true) },
                { "error", ErrorSchema() },
                { "receivedAtUtc", DateSchema() },
                { "persistedAtUtc", DateSchema() },
                { "retryable", new BsonDocument("bsonType", "bool") },
                { "retryCount", IntegerSchema() },
                { "ingestion", IngestionSchema() }
            }
        }
    };

    private static BsonDocument CheckpointV2() => new()
    {
        { "bsonType", "object" },
        {
            "required",
            new BsonArray
            {
                "schemaVersion", "sourceKind", "sourceId", "folderDate", "fileId",
                "relativePath", "position", "workerId", "updatedAtUtc", "version"
            }
        },
        {
            "properties",
            new BsonDocument
            {
                { "schemaVersion", IntegerSchema() },
                { "sourceKind", StringSchema() },
                { "sourceId", StringSchema() },
                { "folderDate", StringSchema() },
                { "fileId", IntegerSchema() },
                { "relativePath", StringSchema() },
                { "position", IntegerSchema() },
                { "workerId", StringSchema() },
                { "updatedAtUtc", DateSchema() },
                { "version", IntegerSchema() }
            }
        }
    };

    private static BsonDocument SourceSchema() => new()
    {
        { "bsonType", "object" },
        { "required", new BsonArray { "producer", "sourceId", "transport", "eventName", "deliveryKind" } },
        {
            "properties",
            new BsonDocument
            {
                { "producer", StringSchema() },
                { "sourceId", StringSchema() },
                { "transport", StringSchema() },
                { "eventName", StringSchema() },
                { "deliveryKind", StringSchema() }
            }
        }
    };

    private static BsonDocument RawPayloadSchema(bool allowStringArguments) => new()
    {
        { "bsonType", "object" },
        { "required", new BsonArray { "format", "sha256", "sizeBytes" } },
        {
            "properties",
            new BsonDocument
            {
                { "format", StringSchema() },
                { "text", StringSchema() },
                { "arguments", new BsonDocument(
                    "bsonType",
                    allowStringArguments
                        ? new BsonArray { "array", "string" }
                        : "array") },
                { "sha256", StringSchema() },
                { "sizeBytes", IntegerSchema() }
            }
        },
        {
            "anyOf",
            new BsonArray
            {
                new BsonDocument("required", new BsonArray { "text" }),
                new BsonDocument("required", new BsonArray { "arguments" })
            }
        }
    };

    private static BsonDocument ParseSchema() => new()
    {
        { "bsonType", "object" },
        { "required", new BsonArray { "status", "parserVersion" } },
        {
            "properties",
            new BsonDocument
            {
                { "status", StringSchema() },
                { "parserVersion", StringSchema() }
            }
        }
    };

    private static BsonDocument ErrorSchema() => new()
    {
        { "bsonType", "object" },
        { "required", new BsonArray { "code", "message", "stage", "parserVersion" } },
        {
            "properties",
            new BsonDocument
            {
                { "code", StringSchema() },
                { "message", StringSchema() },
                { "stage", StringSchema() },
                { "parserVersion", StringSchema() }
            }
        }
    };

    private static BsonDocument IngestionSchema() => new()
    {
        { "bsonType", "object" },
        { "required", new BsonArray { "workerId" } },
        { "properties", new BsonDocument { { "workerId", StringSchema() } } }
    };

    private static BsonDocument ObjectSchema() => new("bsonType", "object");

    private static BsonDocument StringSchema() => new("bsonType", "string");

    private static BsonDocument DateSchema() => new("bsonType", "date");

    private static BsonDocument IntegerSchema() => new("bsonType", new BsonArray { "int", "long" });

    private static BsonDocument PositiveIntegerSchema() => new()
    {
        { "bsonType", new BsonArray { "int", "long" } },
        { "minimum", 1 }
    };
}
