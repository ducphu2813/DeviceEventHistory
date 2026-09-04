using DeviceEventStatistics.Application.History;
using MongoDB.Bson;

namespace DeviceEventStatistics.Infrastructure.MongoDb.Reading;

internal static class MongoHistoryQuery
{
    public static BsonDocument IncrementalFilter(
        DateTimeOffset fromPersistedAtUtc,
        DateTimeOffset toPersistedAtUtc,
        SourceCursor? after,
        IReadOnlyCollection<long>? companyIds,
        IReadOnlyCollection<long>? deviceIds)
    {
        var predicates = new BsonArray
        {
            new BsonDocument("persistedAtUtc", new BsonDocument
            {
                { "$gte", fromPersistedAtUtc.UtcDateTime },
                { "$lte", toPersistedAtUtc.UtcDateTime }
            })
        };
        AddCursorPredicate(predicates, after);
        AddInPredicate(predicates, "companyId", companyIds);
        AddInPredicate(predicates, "device.id", deviceIds);
        return new BsonDocument("$and", predicates);
    }

    public static BsonDocument RangeFilter(
        DateTimeOffset fromTimelineAtUtc,
        DateTimeOffset toTimelineAtUtc,
        SourceCursor? after,
        long? companyId,
        long? deviceId)
    {
        var predicates = new BsonArray
        {
            new BsonDocument("timelineAtUtc", new BsonDocument
            {
                { "$gte", fromTimelineAtUtc.UtcDateTime },
                { "$lt", toTimelineAtUtc.UtcDateTime }
            })
        };
        AddCursorPredicate(predicates, after);
        if (companyId is long tenantId) predicates.Add(new BsonDocument("companyId", tenantId));
        if (deviceId is long targetDeviceId) predicates.Add(new BsonDocument("device.id", targetDeviceId));
        return new BsonDocument("$and", predicates);
    }

    private static void AddCursorPredicate(BsonArray predicates, SourceCursor? after)
    {
        if (after is null) return;

        predicates.Add(new BsonDocument("$or", new BsonArray
        {
            new BsonDocument("persistedAtUtc", new BsonDocument("$gt", after.PersistedAtUtc.UtcDateTime)),
            new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("persistedAtUtc", after.PersistedAtUtc.UtcDateTime),
                new BsonDocument("eventId", new BsonDocument("$gt", after.EventId))
            })
        }));
    }

    private static void AddInPredicate(
        BsonArray predicates,
        string fieldName,
        IReadOnlyCollection<long>? values)
    {
        if (values is { Count: > 0 })
        {
            predicates.Add(new BsonDocument(fieldName, new BsonDocument("$in", new BsonArray(values))));
        }
    }
}
