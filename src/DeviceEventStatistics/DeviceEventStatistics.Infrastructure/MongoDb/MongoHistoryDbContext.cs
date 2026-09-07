using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Domain.Common;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventStatistics.Infrastructure.MongoDb;

public sealed class MongoHistoryDbContext(
    MongoHistoryDatabaseOptions options,
    IMongoClient client)
{
    public IMongoDatabase Database => client.GetDatabase(options.DatabaseName);

    public IMongoCollection<BsonDocument> HistoryCollection =>
        Database.GetCollection<BsonDocument>(options.HistoryCollection);

    public Task PingAsync(CancellationToken cancellationToken) =>
        Database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: cancellationToken);

    public async Task<(DateTimeOffset? OldestPersistedAtUtc, DateTimeOffset? LatestPersistedAtUtc)>
        ReadPersistedBoundsAsync(CancellationToken cancellationToken)
    {
        var validTimestamp = new BsonDocument(
            "persistedAtUtc",
            new BsonDocument("$type", "date"));
        var collection = HistoryCollection;
        var ascending = await collection
            .Find(validTimestamp)
            .Sort(Builders<BsonDocument>.Sort.Ascending("persistedAtUtc"))
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include("persistedAtUtc"))
            .FirstOrDefaultAsync(cancellationToken);
        var descending = await collection
            .Find(validTimestamp)
            .Sort(Builders<BsonDocument>.Sort.Descending("persistedAtUtc"))
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include("persistedAtUtc"))
            .FirstOrDefaultAsync(cancellationToken);

        return (ReadPersistedAtUtc(ascending), ReadPersistedAtUtc(descending));
    }

    public async Task VerifyReadContractAsync(CancellationToken cancellationToken)
    {
        var collectionNames = await (await Database.ListCollectionNamesAsync(cancellationToken: cancellationToken))
            .ToListAsync(cancellationToken);

        if (!collectionNames.Contains(options.HistoryCollection, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_MONGO_COLLECTION_MISSING,
                    options.HistoryCollection));
        }

        var indexNames = await (await HistoryCollection.Indexes.ListAsync(cancellationToken))
            .ToListAsync(cancellationToken);
        var actualIndexNames = indexNames
            .Select(index => index.GetValue("name", string.Empty).AsString)
            .ToHashSet(StringComparer.Ordinal);

        var missingIndexes = options.RequiredHistoryIndexNames
            .Where(indexName => !actualIndexNames.Contains(indexName))
            .ToArray();
        if (missingIndexes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_MONGO_INDEX_MISSING,
                    string.Join(", ", missingIndexes)));
        }
    }

    private static DateTimeOffset? ReadPersistedAtUtc(BsonDocument? document)
    {
        if (document is null || !document.TryGetValue("persistedAtUtc", out var value) ||
            value.BsonType != BsonType.DateTime)
        {
            return null;
        }

        return new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);
    }
}
