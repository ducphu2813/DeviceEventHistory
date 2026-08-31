using System.Diagnostics;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Mapping;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb.Stores;

public sealed class MongoDeviceEventHistoryWriter(
    MongoDbContext context,
    MongoRetryPolicy retryPolicy,
    IIngestionTelemetry? telemetry = null) : IDeviceEventHistoryWriter
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
            deviceEvent.PersistedAtUtc ?? receivedAtUtc,
            workerId);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await retryPolicy.ExecuteAsync(
                token => collection.InsertOneAsync(document, cancellationToken: token),
                cancellationToken);

            telemetry?.RecordHistoryWrite(
                wasAlreadyPersisted: false,
                Stopwatch.GetElapsedTime(startedAt));
            return new PersistenceWriteResult(deviceEvent.EventId, false);
        }
        catch (MongoWriteException exception) when (IsDuplicateKey(exception))
        {
            telemetry?.RecordHistoryWrite(
                wasAlreadyPersisted: true,
                Stopwatch.GetElapsedTime(startedAt));
            return new PersistenceWriteResult(deviceEvent.EventId, true);
        }
    }

    private static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey;
}
