using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Infrastructure.MongoDb.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventStatistics.Infrastructure.MongoDb.Reading;

public sealed class MongoHistoryContractAuditReader(
    MongoHistoryDbContext context,
    HistoryDocumentMapper documentMapper)
    : IHistoryContractAuditReader
{
    public async Task<HistoryAuditResult> ReadAuditPageAsync(
        string? afterSourceDocumentId,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var filter = CreateAuditFilter(afterSourceDocumentId);
        using var cursor = await context.HistoryCollection.FindAsync(
            filter,
            new FindOptions<BsonDocument>
            {
                Projection = MongoHistoryFieldProjection.Definition,
                Sort = new BsonDocument("_id", 1),
                Limit = pageSize,
                BatchSize = pageSize
            },
            cancellationToken);
        var documents = await cursor.ToListAsync(cancellationToken);

        var events = documents.Select(documentMapper.Map).ToArray();
        var nextId = events.LastOrDefault()?.SourceDocumentId ?? afterSourceDocumentId;
        return new HistoryAuditResult(events, nextId, documents.Count < pageSize);
    }

    private static BsonDocument CreateAuditFilter(string? afterSourceDocumentId)
    {
        if (string.IsNullOrWhiteSpace(afterSourceDocumentId) ||
            !ObjectId.TryParse(afterSourceDocumentId, out var objectId))
        {
            return new BsonDocument();
        }

        return new BsonDocument("_id", new BsonDocument("$gt", objectId));
    }
}
