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
        var nextId = documents.LastOrDefault() is BsonDocument lastDocument
            ? ReadCursorId(lastDocument)
            : afterSourceDocumentId;
        return new HistoryAuditResult(events, nextId, documents.Count < pageSize);
    }

    private static BsonDocument CreateAuditFilter(string? afterSourceDocumentId)
    {
        if (string.IsNullOrWhiteSpace(afterSourceDocumentId))
        {
            return new BsonDocument();
        }

        if (ObjectId.TryParse(afterSourceDocumentId, out var objectId))
        {
            return new BsonDocument("_id", new BsonDocument("$gt", objectId));
        }

        return new BsonDocument("_id", new BsonDocument("$gt", afterSourceDocumentId));
    }

    private static string? ReadCursorId(BsonDocument document) =>
        document.GetValue("_id", BsonNull.Value) switch
        {
            BsonObjectId objectId => objectId.Value.ToString(),
            BsonString stringValue when !string.IsNullOrWhiteSpace(stringValue.Value) => stringValue.Value,
            _ => null
        };
}
