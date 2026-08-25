using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb.Stores;

public sealed class MongoDeviceEventHistoryWriter(
    MongoDbContext context,
    MongoRetryPolicy retryPolicy) : IDeviceEventHistoryWriter
{
    public async Task<PersistenceWriteResult> WriteAsync(
        CanonicalDeviceEvent deviceEvent,
        DateTimeOffset receivedAtUtc,
        string workerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var collection = context.GetCollection(AppConst.MongoDb.HistoryCollection);
        var document = CanonicalDeviceEventDocumentMapper.ToDocument(
            deviceEvent,
            receivedAtUtc,
            receivedAtUtc,
            workerId);

        try
        {
            await retryPolicy.ExecuteAsync(
                token => collection.InsertOneAsync(document, cancellationToken: token),
                cancellationToken);

            return new PersistenceWriteResult(deviceEvent.EventId, false);
        }
        catch (MongoWriteException exception) when (IsDuplicateKey(exception))
        {
            return new PersistenceWriteResult(deviceEvent.EventId, true);
        }
    }

    private static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey;
}
