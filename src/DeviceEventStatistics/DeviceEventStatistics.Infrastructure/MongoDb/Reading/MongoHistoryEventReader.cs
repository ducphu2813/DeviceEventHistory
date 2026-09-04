using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Infrastructure.MongoDb.Mapping;
using DeviceEventStatistics.Domain.Common;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventStatistics.Infrastructure.MongoDb.Reading;

public sealed class MongoHistoryEventReader(
    MongoHistoryDbContext context,
    HistoryDocumentMapper documentMapper)
    : IHistoryEventReader
{
    public async Task<HistoryReadResult> ReadPageAsync(
        DateTimeOffset fromPersistedAtUtc,
        DateTimeOffset toPersistedAtUtc,
        SourceCursor? after,
        int pageSize,
        IReadOnlyCollection<long>? companyIds = null,
        IReadOnlyCollection<long>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        if (fromPersistedAtUtc > toPersistedAtUtc)
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_MONGO_RANGE_INVALID);
        }

        using var cursor = await context.HistoryCollection.FindAsync(
            MongoHistoryQuery.IncrementalFilter(
                fromPersistedAtUtc,
                toPersistedAtUtc,
                after,
                companyIds,
                deviceIds),
            new FindOptions<BsonDocument>
            {
                Projection = MongoHistoryFieldProjection.Definition,
                Sort = new BsonDocument("persistedAtUtc", 1).Add("eventId", 1),
                Limit = pageSize,
                BatchSize = pageSize,
                Collation = new Collation("simple")
            },
            cancellationToken);
        var documents = await cursor.ToListAsync(cancellationToken);

        var events = documents.Select(documentMapper.Map).ToArray();
        var nextCursor = FindLastValidCursor(events, after);
        return new HistoryReadResult(
            events,
            nextCursor,
            toPersistedAtUtc,
            documents.Count < pageSize);
    }

    private static SourceCursor? FindLastValidCursor(
        IReadOnlyList<HistoryEvent> events,
        SourceCursor? current)
    {
        for (var index = events.Count - 1; index >= 0; index--)
        {
            var cursor = SourceCursor.From(events[index]);
            if (cursor is not null && (current is null || cursor.IsAfter(current))) return cursor;
        }

        return current;
    }
}
