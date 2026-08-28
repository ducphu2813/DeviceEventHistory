using DeviceEventHistory.Domain.Failures;
using DeviceEventHistory.Domain.Common;
using MongoDB.Bson;

namespace DeviceEventHistory.Infrastructure.MongoDb.Mapping;

internal static class IngestionFailureDocumentMapper
{
    public static BsonDocument ToDocument(
        CanonicalIngestionFailure failure,
        DateTimeOffset receivedAtUtc,
        string workerId) => new()
    {
        { "_id", ObjectId.GenerateNewId() },
        { "failureId", failure.FailureId },
        { "companyId", failure.CompanyId ?? throw new InvalidOperationException(
            AppConst.Messages.MSG_RAW_LOG_FAILURE_COMPANY_ID_REQUIRED) },
        { "source", new BsonDocument
            {
                { "sourceId", failure.Source.SourceId },
                { "kind", failure.SourceKind },
                { "producer", failure.Source.Producer },
                { "fileId", failure.Source.FileId ?? throw new InvalidOperationException(
                    AppConst.Messages.MSG_RAW_LOG_FAILURE_FILE_ID_REQUIRED) },
                { "fileName", failure.Source.FileName ?? throw new InvalidOperationException(
                    AppConst.Messages.MSG_RAW_LOG_FAILURE_FILE_NAME_REQUIRED) },
                { "relativePath", failure.Source.RelativePath ?? throw new InvalidOperationException(
                    AppConst.Messages.MSG_RAW_LOG_FAILURE_RELATIVE_PATH_REQUIRED) },
                { "folderDate", failure.Source.FolderDate is DateOnly folderDate
                    ? MongoDocumentValue.DateOnly(folderDate)
                    : throw new InvalidOperationException(
                        AppConst.Messages.MSG_RAW_LOG_FAILURE_FOLDER_DATE_REQUIRED) },
                { "offsetStart", failure.Source.OffsetStart ?? throw new InvalidOperationException(
                    AppConst.Messages.MSG_RAW_LOG_FAILURE_START_OFFSET_REQUIRED) },
                { "offsetEnd", failure.Source.OffsetEnd ?? throw new InvalidOperationException(
                    AppConst.Messages.MSG_RAW_LOG_FAILURE_END_OFFSET_REQUIRED) }
            } },
        { "rawPayload", new BsonDocument
            {
                { "format", failure.RawPayload.Format },
                { "text", failure.RawPayload.Text ?? throw new InvalidOperationException(
                    AppConst.Messages.MSG_RAW_LOG_FAILURE_RAW_TEXT_REQUIRED) },
                { "sha256", failure.RawPayload.Sha256 }
            } },
        { "error", new BsonDocument
            {
                { "code", failure.Error.Code },
                { "message", failure.Error.Message },
                { "parserVersion", failure.Error.ParserVersion },
            } },
        { "receivedAtUtc", MongoDocumentValue.DateTimeOffset(receivedAtUtc) },
        { "retryable", failure.Retryable },
        { "resolvedAtUtc", BsonNull.Value },
        { "ingestion", new BsonDocument("workerId", workerId) }
    };
}
