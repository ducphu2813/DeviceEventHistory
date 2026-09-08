using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb.Indexes;

public sealed class MongoIndexInitializer(
    MongoDbContext context,
    MongoRetryPolicy retryPolicy,
    MongoRetentionSettings retentionSettings)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await context.PingAsync(cancellationToken);
        await EnsureCollectionSchemasAsync(cancellationToken);
        await CreateHistoryIndexesAsync(cancellationToken);
        await CreateFailureIndexesAsync(cancellationToken);
        await CreateCheckpointIndexesAsync(cancellationToken);
    }

    private async Task EnsureCollectionSchemasAsync(CancellationToken cancellationToken)
    {
        await EnsureCollectionSchemaAsync(AppConst.MongoDb.HistoryCollection, MongoSchemaValidator.History(), cancellationToken);
        await EnsureCollectionSchemaAsync(AppConst.MongoDb.FailureCollection, MongoSchemaValidator.Failure(), cancellationToken);
        await EnsureCollectionSchemaAsync(AppConst.MongoDb.CheckpointCollection, MongoSchemaValidator.Checkpoint(), cancellationToken);
    }

    private async Task EnsureCollectionSchemaAsync(
        string collectionName,
        BsonDocument validator,
        CancellationToken cancellationToken)
    {
        await retryPolicy.ExecuteAsync(
            async token =>
            {
                try
                {
                    await context.Database.CreateCollectionAsync(
                        collectionName,
                        new CreateCollectionOptions<BsonDocument>
                        {
                            Validator = validator,
                            ValidationLevel = DocumentValidationLevel.Moderate,
                            ValidationAction = DocumentValidationAction.Error
                        },
                        token);
                }
                catch (MongoCommandException exception) when (exception.Code == 48)
                {
                    await context.Database.RunCommandAsync<BsonDocument>(
                        new BsonDocument
                        {
                            { "collMod", collectionName },
                            { "validator", validator },
                            { "validationLevel", "moderate" },
                            { "validationAction", "error" }
                        },
                        cancellationToken: token);
                }
            },
            cancellationToken);
    }

    private async Task CreateHistoryIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = context.GetCollection(AppConst.MongoDb.HistoryCollection);
        var existingIndexes = await ListIndexesAsync(collection, cancellationToken);
        var models = new[]
        {
            UniqueIndex("eventId", AppConst.MongoDb.EventIdIndexName),

            // Keep the legacy V1 indexes for existing query consumers.
            DescendingIndex("occurredAtUtc", AppConst.MongoDb.HistoryOccurredAtIndexName),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryDeviceOccurredAtIndexName, "device.id", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryGateOccurredAtIndexName, "device.gateId", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryTagOccurredAtIndexName, "facts.tagRead.tagId", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryCategoryOccurredAtIndexName, "category", "occurredAtUtc"),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryParseReceivedAtIndexName, "parse.status", "receivedAtUtc"),

            // V2 query indexes use the effective timeline and sparse filters.
            CompoundDescendingIndex(AppConst.MongoDb.HistoryCompanyTimelineIndexName, "companyId", "timelineAtUtc", V2Filter("companyId")),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryCompanyCategoryTimelineIndexName, "companyId", "category", "timelineAtUtc", V2Filter("companyId")),
            CompoundDescendingIndex(AppConst.MongoDb.HistorySourceKindReceivedAtIndexName, "sourceKind", "receivedAtUtc", V2Filter()),
            CompoundDescendingIndex(AppConst.MongoDb.HistorySourceReceivedAtIndexName, "source.sourceId", "receivedAtUtc", V2Filter("source.sourceId")),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryEventNameReceivedAtIndexName, "source.eventName", "receivedAtUtc", V2Filter("source.eventName")),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryDeviceTimelineIndexName, "device.id", "timelineAtUtc", V2Filter("device.id")),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryGateTimelineIndexName, "device.gateId", "timelineAtUtc", V2Filter("device.gateId")),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryTagTimelineIndexName, "facts.tagRead.tagId", "timelineAtUtc", V2Filter("facts.tagRead.tagId")),
            CompoundDescendingIndex(AppConst.MongoDb.HistoryParseStatusReceivedAtV2IndexName, "parse.status", "receivedAtUtc", V2Filter("parse.status")),

            // Statistics reads advance by persisted-event cursor, optionally scoped to a company and device.
            CompoundIndex(
                AppConst.MongoDb.HistoryStatisticsCursorIndexName,
                new BsonDocument
                {
                    { "persistedAtUtc", 1 },
                    { "eventId", 1 }
                }),
            CompoundIndex(
                AppConst.MongoDb.HistoryStatisticsScopedCursorIndexName,
                new BsonDocument
                {
                    { "companyId", 1 },
                    { "device.id", 1 },
                    { "persistedAtUtc", 1 },
                    { "eventId", 1 }
                }),

        }.ToList();

        models.AddRange(GetFileTraceMigrationIndexes(
            existingIndexes,
            AppConst.MongoDb.HistorySourceOffsetIndexName,
            AppConst.MongoDb.HistorySourceOffsetV2IndexName));

        await CreateMissingIndexesAsync(collection, existingIndexes, models, cancellationToken);
        await EnsureTtlIndexAsync(
            collection,
            existingIndexes,
            AppConst.MongoDb.HistoryRetentionTtlIndexName,
            retentionSettings.HistoryRetentionDays,
            cancellationToken);
    }

    private async Task CreateFailureIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = context.GetCollection(AppConst.MongoDb.FailureCollection);
        var existingIndexes = await ListIndexesAsync(collection, cancellationToken);
        var models = new[]
        {
            UniqueIndex("failureId", AppConst.MongoDb.FailureIdIndexName),
            CompoundDescendingIndex(AppConst.MongoDb.FailureCodeReceivedAtIndexName, "error.code", "receivedAtUtc"),
            CompoundIndex(
                AppConst.MongoDb.FailureSourceErrorReceivedAtIndexName,
                new BsonDocument
                {
                    { "sourceKind", 1 },
                    { "source.sourceId", 1 },
                    { "source.eventName", 1 },
                    { "error.code", 1 },
                    { "error.stage", 1 },
                    { "receivedAtUtc", -1 }
                },
                V2Filter("source.sourceId", "source.eventName", "error.code", "error.stage")),
            AscendingIndex("resolvedAtUtc", AppConst.MongoDb.FailureResolvedAtIndexName)
        }.ToList();

        models.AddRange(GetFileTraceMigrationIndexes(
            existingIndexes,
            AppConst.MongoDb.FailureSourceOffsetIndexName,
            AppConst.MongoDb.FailureSourceOffsetV2IndexName));

        await CreateMissingIndexesAsync(collection, existingIndexes, models, cancellationToken);
        await EnsureTtlIndexAsync(
            collection,
            existingIndexes,
            AppConst.MongoDb.FailureRetentionTtlIndexName,
            retentionSettings.FailureRetentionDays,
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

    private async Task EnsureTtlIndexAsync(
        IMongoCollection<BsonDocument> collection,
        IReadOnlyList<BsonDocument> existingIndexes,
        string canonicalIndexName,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var retentionSeconds = checked(retentionDays * 24 * 60 * 60);
        var ttlIndex = existingIndexes.FirstOrDefault(IsPersistedAtTtlIndex);
        if (ttlIndex is null)
        {
            await retryPolicy.ExecuteAsync(
                token => collection.Indexes.CreateOneAsync(
                    new CreateIndexModel<BsonDocument>(
                        new BsonDocumentIndexKeysDefinition<BsonDocument>(
                            new BsonDocument("persistedAtUtc", 1)),
                        new CreateIndexOptions
                        {
                            Name = canonicalIndexName,
                            ExpireAfter = TimeSpan.FromSeconds(retentionSeconds)
                        }),
                    cancellationToken: token),
                cancellationToken);
            return;
        }

        var indexName = ttlIndex["name"].AsString;
        var currentSeconds = ttlIndex["expireAfterSeconds"].ToInt64();
        if (currentSeconds == retentionSeconds)
        {
            return;
        }

        await retryPolicy.ExecuteAsync(
            token => context.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    { "collMod", collection.CollectionNamespace.CollectionName },
                    {
                        "index",
                        new BsonDocument
                        {
                            { "name", indexName },
                            { "expireAfterSeconds", retentionSeconds }
                        }
                    }
                },
                cancellationToken: token),
            cancellationToken);
    }

    private static bool IsPersistedAtTtlIndex(BsonDocument index)
    {
        if (!index.TryGetValue("expireAfterSeconds", out _) ||
            !index.TryGetValue("key", out var keyValue) ||
            keyValue is not BsonDocument key ||
            key.ElementCount != 1 ||
            !key.TryGetValue("persistedAtUtc", out var direction))
        {
            return false;
        }

        return direction.ToInt32() == 1;
    }

    private static CreateIndexModel<BsonDocument> CompoundDescendingIndex(
        string name,
        string firstField,
        string secondField,
        BsonDocument? partialFilter = null) =>
        CompoundIndex(
            name,
            new BsonDocument
            {
                { firstField, 1 },
                { secondField, -1 }
            },
            partialFilter);

    private static CreateIndexModel<BsonDocument> CompoundDescendingIndex(
        string name,
        string firstField,
        string secondField,
        string thirdField,
        BsonDocument? partialFilter = null) =>
        CompoundIndex(
            name,
            new BsonDocument
            {
                { firstField, 1 },
                { secondField, 1 },
                { thirdField, -1 }
            },
            partialFilter);

    private static CreateIndexModel<BsonDocument> CompoundIndex(
        string name,
        BsonDocument keys,
        BsonDocument? partialFilter = null) =>
        new(
            new BsonDocumentIndexKeysDefinition<BsonDocument>(keys),
            CreateIndexOptions<BsonDocument>(name, partialFilter));

    private static CreateIndexModel<BsonDocument> FileTraceIndex(
        string name,
        BsonDocument? partialFilter = null) =>
        CompoundIndex(
            name,
            new BsonDocument
            {
                { "source.sourceId", 1 },
                { "source.folderDate", 1 },
                { "source.fileId", 1 },
                { "source.offsetStart", 1 }
            },
            partialFilter);

    private static IReadOnlyList<CreateIndexModel<BsonDocument>> GetFileTraceMigrationIndexes(
        IReadOnlyList<BsonDocument> existingIndexes,
        string legacyIndexName,
        string v2IndexName)
    {
        var legacyIndex = existingIndexes.FirstOrDefault(index =>
            index.GetValue("name", string.Empty).AsString == legacyIndexName);
        if (legacyIndex is null)
        {
            return
            [
                FileTraceIndex(
                    legacyIndexName,
                    V2Filter("source.folderDate", "source.fileId", "source.offsetStart"))
            ];
        }

        return HasPartialFilter(legacyIndex)
            ? []
            : [FileTraceIndex(v2IndexName, V2Filter("source.folderDate", "source.fileId", "source.offsetStart"))];
    }

    private async Task<IReadOnlyList<BsonDocument>> ListIndexesAsync(
        IMongoCollection<BsonDocument> collection,
        CancellationToken cancellationToken)
    {
        return await retryPolicy.ExecuteAsync(
            async token => await (await collection.Indexes.ListAsync(token)).ToListAsync(token),
            cancellationToken);
    }

    private async Task CreateMissingIndexesAsync(
        IMongoCollection<BsonDocument> collection,
        IReadOnlyList<BsonDocument> existingIndexes,
        IEnumerable<CreateIndexModel<BsonDocument>> models,
        CancellationToken cancellationToken)
    {
        var existingNames = existingIndexes
            .Select(index => index.GetValue("name", string.Empty).AsString)
            .ToHashSet(StringComparer.Ordinal);
        var missingModels = models
            .Where(model => model.Options.Name is not null
                && !existingNames.Contains(model.Options.Name))
            .ToArray();
        if (missingModels.Length == 0)
        {
            return;
        }

        await retryPolicy.ExecuteAsync(
            token => collection.Indexes.CreateManyAsync(missingModels, token),
            cancellationToken);
    }

    private static bool HasPartialFilter(BsonDocument index) =>
        index.TryGetValue("partialFilterExpression", out var value)
        && !value.IsBsonNull;

    private static CreateIndexOptions<TDocument> CreateIndexOptions<TDocument>(
        string name,
        BsonDocument? partialFilter = null) =>
        new()
        {
            Name = name,
            PartialFilterExpression = partialFilter is null
                ? null
                : new BsonDocumentFilterDefinition<TDocument>(partialFilter)
        };

    private static BsonDocument V2Filter(params string[] requiredPaths)
    {
        var expressions = new BsonArray
        {
            new BsonDocument("schemaVersion", AppConst.SchemaVersions.CanonicalV2)
        };

        foreach (var path in requiredPaths)
        {
            expressions.Add(new BsonDocument(path, new BsonDocument("$exists", true)));
        }

        return expressions.Count == 1
            ? expressions[0].AsBsonDocument
            : new BsonDocument("$and", expressions);
    }
}
