using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Failures;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DeviceEventHistory.Infrastructure.MongoDb.Mapping;

internal static class IngestionFailureDocumentMapper
{
    public static BsonDocument ToDocument(
        CanonicalIngestionFailure failure,
        DateTimeOffset receivedAtUtc,
        string workerId)
    {
        if (failure.SourceKind == AppConst.SourceKinds.RfidAntennaFile
            && failure.CompanyId is not > 0)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_RAW_LOG_FAILURE_COMPANY_ID_REQUIRED);
        }

        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "failureId", failure.FailureId },
            { "schemaVersion", failure.SchemaVersion },
            { "sourceKind", failure.SourceKind },
            { "companyId", failure.CompanyId is int companyId
                ? new BsonInt32(companyId)
                : BsonNull.Value },
            { "source", MapSource(failure) },
            { "rawPayload", MapRawPayload(failure) },
            { "error", MapError(failure.Error) },
            { "receivedAtUtc", MongoDocumentValue.DateTimeOffset(
                failure.ReceivedAtUtc ?? receivedAtUtc) },
            { "persistedAtUtc", MongoDocumentValue.DateTimeOffset(
                failure.PersistedAtUtc ?? receivedAtUtc) },
            { "retryable", failure.Retryable },
            { "retryCount", failure.RetryCount },
            { "resolvedAtUtc", MongoDocumentValue.DateTimeOffset(failure.ResolvedAtUtc) },
            { "resolution", MongoDocumentValue.String(failure.Resolution) },
            { "ingestion", new BsonDocument
                {
                    { "workerId", workerId },
                    { "attempt", failure.Ingestion?.Attempt ?? 1 },
                    { "processingDurationMs", MongoDocumentValue.Int64(
                        failure.Ingestion?.ProcessingDurationMs) }
                } }
        };

        return document;
    }

    private static BsonDocument MapSource(CanonicalIngestionFailure failure)
    {
        var source = failure.Source;
        var document = new BsonDocument
        {
            { "sourceId", source.SourceId },
            { "kind", failure.SourceKind },
            { "producer", source.Producer }
        };
        AddOptional(document, "transport", MongoDocumentValue.String(source.Transport));
        AddOptional(document, "eventName", MongoDocumentValue.String(source.EventName));
        AddOptional(document, "deliveryKind", MongoDocumentValue.String(source.DeliveryKind));
        AddOptional(document, "connectionGeneration", MongoDocumentValue.String(
            source.ConnectionGeneration));
        AddOptional(document, "receiveSequence", MongoDocumentValue.Int64(source.ReceiveSequence));

        if (failure.SourceKind == AppConst.SourceKinds.RfidAntennaFile)
        {
            AddRequired(document, "fileId", source.FileId, AppConst.Messages.MSG_RAW_LOG_FAILURE_FILE_ID_REQUIRED);
            AddRequired(document, "fileName", source.FileName, AppConst.Messages.MSG_RAW_LOG_FAILURE_FILE_NAME_REQUIRED);
            AddRequired(document, "relativePath", source.RelativePath, AppConst.Messages.MSG_RAW_LOG_FAILURE_RELATIVE_PATH_REQUIRED);
            AddRequired(document, "folderDate", source.FolderDate is DateOnly folderDate
                ? MongoDocumentValue.DateOnly(folderDate)
                : null, AppConst.Messages.MSG_RAW_LOG_FAILURE_FOLDER_DATE_REQUIRED);
            AddRequired(document, "offsetStart", MongoDocumentValue.Int64(source.OffsetStart), AppConst.Messages.MSG_RAW_LOG_FAILURE_START_OFFSET_REQUIRED);
            AddRequired(document, "offsetEnd", MongoDocumentValue.Int64(source.OffsetEnd), AppConst.Messages.MSG_RAW_LOG_FAILURE_END_OFFSET_REQUIRED);
        }
        else
        {
            AddOptional(document, "fileId", MongoDocumentValue.Int64(source.FileId));
            AddOptional(document, "fileName", MongoDocumentValue.String(source.FileName));
            AddOptional(document, "relativePath", MongoDocumentValue.String(source.RelativePath));
            AddOptional(document, "folderDate", source.FolderDate is DateOnly folderDate
                ? MongoDocumentValue.DateOnly(folderDate)
                : null);
            AddOptional(document, "offsetStart", MongoDocumentValue.Int64(source.OffsetStart));
            AddOptional(document, "offsetEnd", MongoDocumentValue.Int64(source.OffsetEnd));
        }

        return document;
    }

    private static BsonDocument MapRawPayload(CanonicalIngestionFailure failure)
    {
        var rawPayload = failure.RawPayload;
        var document = new BsonDocument { { "format", rawPayload.Format } };
        if (failure.SourceKind == AppConst.SourceKinds.RfidAntennaFile)
        {
            AddRequired(
                document,
                "text",
                MongoDocumentValue.String(rawPayload.Text),
                AppConst.Messages.MSG_RAW_LOG_FAILURE_RAW_TEXT_REQUIRED);
        }
        else
        {
            AddOptional(document, "text", MongoDocumentValue.String(rawPayload.Text));
            AddOptional(document, "arguments", ParseArguments(rawPayload.ArgumentsJson));
        }

        AddOptional(document, "sha256", rawPayload.Sha256);
        AddOptional(document, "sizeBytes", MongoDocumentValue.Int64(rawPayload.SizeBytes));
        return document;
    }

    private static BsonDocument MapError(CanonicalIngestionFailure.ErrorContext error) => new()
    {
        { "code", error.Code },
        { "message", error.Message },
        { "stage", error.Stage },
        { "parserVersion", error.ParserVersion },
        { "details", MongoDocumentValue.StringArray(error.Details) }
    };

    private static BsonValue? ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return null;
        }

        try
        {
            return BsonSerializer.Deserialize<BsonValue>(argumentsJson);
        }
        catch (Exception exception) when (
            exception is BsonSerializationException
                or FormatException
                or InvalidOperationException)
        {
            return new BsonString(argumentsJson);
        }
    }

    private static void AddRequired(
        BsonDocument document,
        string name,
        BsonValue? value,
        string message)
    {
        if (value is null || value.IsBsonNull)
        {
            throw new InvalidOperationException(message);
        }

        document.Add(name, value);
    }

    private static void AddOptional(
        BsonDocument document,
        string name,
        BsonValue? value)
    {
        if (value is not null && !value.IsBsonNull)
        {
            document.Add(name, value);
        }
    }
}
