using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb.Indexes;

public sealed class MongoIndexInitializer(
    MongoDbContext context,
    MongoRetryPolicy retryPolicy)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await context.PingAsync(cancellationToken);
        await CreateHistoryIndexesAsync(cancellationToken);
        await CreateFailureIndexesAsync(cancellationToken);
        await CreateCheckpointIndexesAsync(cancellationToken);
    }

    private async Task CreateHistoryIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = context.GetCollection(AppConst.MongoDb.HistoryCollection);
        var models = new[]
        {
            UniqueIndex("eventId", AppConst.MongoDb.EventIdIndexName),
            DescendingIndex("occurredAtUtc", AppConst.MongoDb.HistoryOccurredAtIndexName),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryDeviceOccurredAtIndexName, "device.id", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryGateOccurredAtIndexName, "device.gateId", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryTagOccurredAtIndexName, "facts.tagRead.tagId", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryCategoryOccurredAtIndexName, "category", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryParseReceivedAtIndexName, "parse.status", "receivedAtUtc"),
            new CreateIndexModel<BsonDocument>(
                new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument
                {
                    { "source.sourceId", 1 },
                    { "source.folderDate", 1 },
                    { "source.fileId", 1 },
                    { "source.offsetStart", 1 }
                }),
                new CreateIndexOptions { Name = AppConst.MongoDb.HistorySourceOffsetIndexName })
        };

        await retryPolicy.ExecuteAsync(
            token => collection.Indexes.CreateManyAsync(models, token),
            cancellationToken);
    }

    private async Task CreateFailureIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = context.GetCollection(AppConst.MongoDb.FailureCollection);
        var models = new[]
        {
            UniqueIndex("failureId", AppConst.MongoDb.FailureIdIndexName),
            new CreateIndexModel<BsonDocument>(
                new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument
                {
                    { "source.sourceId", 1 },
                    { "source.folderDate", 1 },
                    { "source.fileId", 1 },
                    { "source.offsetStart", 1 }
                }),
                new CreateIndexOptions { Name = AppConst.MongoDb.FailureSourceOffsetIndexName }),
            CompoundDescendingIndex(AppConst.MongoDb.FailureCodeReceivedAtIndexName, "error.code", "receivedAtUtc"),
            AscendingIndex("resolvedAtUtc", AppConst.MongoDb.FailureResolvedAtIndexName)
        };

        await retryPolicy.ExecuteAsync(
            token => collection.Indexes.CreateManyAsync(models, token),
            cancellationToken);
    }

    private async Task CreateCheckpointIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = context.GetCollection(AppConst.MongoDb.CheckpointCollection);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument
                {
                    { "sourceId", 1 },
                    { "folderDate", 1 },
                    { "fileId", 1 },
                    { "relativePath", 1 }
                }),
                new CreateIndexOptions
                {
                    Name = AppConst.MongoDb.CheckpointSourceIdentityIndexName,
                    Unique = true
                }),
            DescendingIndex("updatedAtUtc", AppConst.MongoDb.CheckpointUpdatedAtIndexName)
        };

        await retryPolicy.ExecuteAsync(
            token => collection.Indexes.CreateManyAsync(models, token),
            cancellationToken);
    }

    private static CreateIndexModel<BsonDocument> UniqueIndex(string field, string name) =>
        new(
            new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument(field, 1)),
            new CreateIndexOptions { Name = name, Unique = true });

    private static CreateIndexModel<BsonDocument> AscendingIndex(string field, string name) =>
        new(
            new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument(field, 1)),
            new CreateIndexOptions { Name = name });

    private static CreateIndexModel<BsonDocument> DescendingIndex(string field, string name) =>
        new(
            new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument(field, -1)),
            new CreateIndexOptions { Name = name });

    private static CreateIndexModel<BsonDocument> CompoundDescendingIndex(
        string name,
        string firstField,
        string secondField) =>
        new(
            new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument
            {
                { firstField, 1 },
                { secondField, -1 }
            }),
            new CreateIndexOptions { Name = name });
}
