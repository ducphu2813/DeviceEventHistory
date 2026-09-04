using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Infrastructure.MongoDb.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventStatistics.Infrastructure.MongoDb.Reading;

public sealed class MongoHistoryRangeReader(
    MongoHistoryDbContext context,
    HistoryDocumentMapper documentMapper)
    : IHistoryRangeReader
{
    public async Task<HistoryReadResult> ReadRangePageAsync(
        DateTimeOffset fromTimelineAtUtc,
        DateTimeOffset toTimelineAtUtc,
        SourceCursor? after,
        int pageSize,
        long? companyId = null,
        long? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        using var cursor = await context.HistoryCollection.FindAsync(
            MongoHistoryQuery.RangeFilter(
                fromTimelineAtUtc,
                toTimelineAtUtc,
                after,
                companyId,
                deviceId),
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
        var nextCursor = events
            .Select(SourceCursor.From)
            .LastOrDefault(cursor => cursor is not null) ?? after;
        return new HistoryReadResult(
            events,
            nextCursor,
            toTimelineAtUtc,
            documents.Count < pageSize);
    }
}
