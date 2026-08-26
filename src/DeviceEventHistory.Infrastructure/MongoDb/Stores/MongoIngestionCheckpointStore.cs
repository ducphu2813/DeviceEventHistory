using System.Diagnostics;

using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Infrastructure.MongoDb.Execution;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb.Stores;

public sealed class MongoIngestionCheckpointStore(
    MongoDbContext context,
    MongoRetryPolicy retryPolicy,
    IIngestionTelemetry? telemetry = null) : IIngestionCheckpointStore
{
    public async Task<IngestionCheckpoint?> LoadAsync(
        IngestionCheckpointKey key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        var collection = context.GetCollection(AppConst.MongoDb.CheckpointCollection);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", key.DocumentId);
        var document = await retryPolicy.ExecuteAsync(
            token => collection.Find(filter).FirstOrDefaultAsync(token),
            cancellationToken);

        return document is null ? null : FromDocument(document, key);
    }

    public async Task<CheckpointAdvanceResult> AdvanceAsync(
        IngestionCheckpointKey key,
        long expectedVersion,
        CheckpointAdvanceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);
        if (expectedVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        if (request.Position < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                AppConst.Messages.MSG_CHECKPOINT_POSITION_INVALID);
        }

        var collection = context.GetCollection(AppConst.MongoDb.CheckpointCollection);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", key.DocumentId),
            Builders<BsonDocument>.Filter.Eq("version", expectedVersion));
        var updatedAtUtc = new BsonDateTime(request.UpdatedAtUtc.UtcDateTime);
        BsonValue lastEventId = request.LastEventId is null
            ? BsonNull.Value
            : new BsonString(request.LastEventId);
        BsonValue observedFileLength = request.ObservedFileLength.HasValue
            ? new BsonInt64(request.ObservedFileLength.Value)
            : BsonNull.Value;
        var update = Builders<BsonDocument>.Update
            .Set("sourceId", key.SourceId)
            .Set("folderDate", key.FolderDate.ToString(AppConst.MongoDb.CheckpointDateFormat))
            .Set("fileId", key.FileId)
            .Set("relativePath", key.RelativePath)
            .Set("position", request.Position)
            .Set("lastEventId", lastEventId)
            .Set("lastRecordHash", request.LastRecordHash)
            .Set("observedFileLength", observedFileLength)
            .Set("workerId", request.WorkerId)
            .Set("updatedAtUtc", updatedAtUtc)
            .Set("version", expectedVersion + 1);

        UpdateResult result;
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            result = await retryPolicy.ExecuteAsync(
                token => collection.UpdateOneAsync(
                    filter,
                    update,
                    new UpdateOptions { IsUpsert = true },
                    token),
                cancellationToken);
        }
        catch (MongoWriteException exception) when (IsDuplicateKey(exception))
        {
            telemetry?.RecordPersistenceLatency(
                AppConst.Observability.OperationCheckpointAdvance,
                Stopwatch.GetElapsedTime(startedAt));
            return new CheckpointAdvanceResult(CheckpointAdvanceStatus.Conflict, null);
        }

        if (result.MatchedCount == 0 && result.UpsertedId is null)
        {
            telemetry?.RecordPersistenceLatency(
                AppConst.Observability.OperationCheckpointAdvance,
                Stopwatch.GetElapsedTime(startedAt));
            return new CheckpointAdvanceResult(CheckpointAdvanceStatus.Conflict, null);
        }

        telemetry?.RecordPersistenceLatency(
            AppConst.Observability.OperationCheckpointAdvance,
            Stopwatch.GetElapsedTime(startedAt));

        var checkpoint = new IngestionCheckpoint
        {
            Key = key,
            Position = request.Position,
            LastEventId = request.LastEventId,
            LastRecordHash = request.LastRecordHash,
            ObservedFileLength = request.ObservedFileLength,
            WorkerId = request.WorkerId,
            UpdatedAtUtc = request.UpdatedAtUtc,
            Version = expectedVersion + 1
        };

        return new CheckpointAdvanceResult(CheckpointAdvanceStatus.Advanced, checkpoint);
    }

    private static IngestionCheckpoint FromDocument(
        BsonDocument document,
        IngestionCheckpointKey key) => new()
    {
        Key = key,
        Position = document.GetValue("position", 0).ToInt64(),
        LastEventId = GetNullableString(document, "lastEventId"),
        LastRecordHash = GetNullableString(document, "lastRecordHash"),
        ObservedFileLength = GetNullableInt64(document, "observedFileLength"),
        WorkerId = GetNullableString(document, "workerId"),
        UpdatedAtUtc = new DateTimeOffset(document.GetValue("updatedAtUtc").ToUniversalTime(), TimeSpan.Zero),
        Version = document.GetValue("version", 0).ToInt64()
    };

    private static string? GetNullableString(BsonDocument document, string fieldName) =>
        document.TryGetValue(fieldName, out var value) && !value.IsBsonNull
            ? value.AsString
            : null;

    private static long? GetNullableInt64(BsonDocument document, string fieldName) =>
        document.TryGetValue(fieldName, out var value) && !value.IsBsonNull
            ? value.ToInt64()
            : null;

    private static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey;
}
