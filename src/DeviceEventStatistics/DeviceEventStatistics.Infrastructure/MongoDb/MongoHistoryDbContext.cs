using DeviceEventStatistics.Infrastructure.Configuration;
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

    public async Task VerifyReadContractAsync(CancellationToken cancellationToken)
    {
        var collectionNames = await (await Database.ListCollectionNamesAsync(cancellationToken: cancellationToken))
            .ToListAsync(cancellationToken);

        if (!collectionNames.Contains(options.HistoryCollection, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"STAT-MONGO-COLLECTION-MISSING: History collection '{options.HistoryCollection}' was not found.");
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
                $"STAT-MONGO-INDEX-MISSING: Required history indexes are missing: {string.Join(", ", missingIndexes)}.");
        }
    }
}
