using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb;

public sealed class MongoDbContext
{
    private readonly MongoDbOptions options;
    private readonly IMongoClient client;

    public MongoDbContext(MongoDbOptions options)
    {
        this.options = options;
        client = new MongoClient(this.options.ConnectionString);
    }

    public IMongoDatabase Database => client.GetDatabase(options.DatabaseName);

    public IMongoCollection<BsonDocument> GetCollection(string collectionName) =>
        Database.GetCollection<BsonDocument>(collectionName);

    public Task PingAsync(CancellationToken cancellationToken) =>
        Database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: cancellationToken);

}
