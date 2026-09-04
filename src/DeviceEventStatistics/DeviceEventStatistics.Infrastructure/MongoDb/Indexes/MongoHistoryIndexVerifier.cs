using DeviceEventStatistics.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventStatistics.Infrastructure.MongoDb.Indexes;

public sealed class MongoHistoryIndexVerifier(
    MongoHistoryDbContext context)
{
    public const string CursorIndexName = "ix_statistics_persisted_event_id";

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        using var cursor = await context.HistoryCollection.Indexes.ListAsync(cancellationToken);
        var indexes = await cursor.ToListAsync(cancellationToken);
        var cursorIndex = indexes.FirstOrDefault(index =>
            index.GetValue("name", string.Empty).AsString == CursorIndexName);
        if (cursorIndex is null || !HasCursorKeys(cursorIndex))
        {
            throw new InvalidOperationException(
                $"STAT-MONGO-CURSOR-INDEX-MISSING: History index '{CursorIndexName}' must contain persistedAtUtc ASC, eventId ASC.");
        }
    }

    private static bool HasCursorKeys(BsonDocument index)
    {
        if (!index.TryGetValue("key", out var keyValue) || keyValue is not BsonDocument key)
        {
            return false;
        }

        return key.ElementCount == 2 &&
            key.GetValue("persistedAtUtc", 0).ToInt32() == 1 &&
            key.GetValue("eventId", 0).ToInt32() == 1;
    }
}
