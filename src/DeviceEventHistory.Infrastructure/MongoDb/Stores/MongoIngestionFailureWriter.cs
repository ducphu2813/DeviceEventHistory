using System.Diagnostics;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using DeviceEventHistory.Infrastructure.MongoDb.Mapping;
using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Failures;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb.Stores;

public sealed class MongoIngestionFailureWriter(
    MongoDbContext context,
    MongoRetryPolicy retryPolicy,
    IIngestionTelemetry? telemetry = null) : IIngestionFailureWriter
{
    public async Task<PersistenceWriteResult> WriteAsync(
        CanonicalIngestionFailure failure,
        DateTimeOffset receivedAtUtc,
        string workerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var collection = context.GetCollection(AppConst.MongoDb.FailureCollection);
        var document = IngestionFailureDocumentMapper.ToDocument(failure, receivedAtUtc, workerId);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await retryPolicy.ExecuteAsync(
                token => collection.InsertOneAsync(document, cancellationToken: token),
                cancellationToken);

            telemetry?.RecordFailureWrite(
                wasAlreadyPersisted: false,
                Stopwatch.GetElapsedTime(startedAt));
            return new PersistenceWriteResult(failure.FailureId, false);
        }
        catch (MongoWriteException exception) when (IsDuplicateKey(exception))
        {
            telemetry?.RecordFailureWrite(
                wasAlreadyPersisted: true,
                Stopwatch.GetElapsedTime(startedAt));
            return new PersistenceWriteResult(failure.FailureId, true);
        }
    }

    private static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey;
}
