using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;
using MongoDB.Bson;

namespace DeviceEventHistory.Infrastructure.MongoDb.Mapping;

internal static class IngestionFailureDocumentMapper
{
    public static BsonDocument ToDocument(
        RawRecordProcessingResult.CanonicalIngestionFailure failure,
        DateTimeOffset receivedAtUtc,
        string workerId) => new()
    {
        { "_id", ObjectId.GenerateNewId() },
        { "failureId", failure.FailureId },
        { "companyId", failure.Context.CompanyId },
        { "source", new BsonDocument
            {
                { "sourceId", failure.Context.SourceId },
                { "kind", AppConst.RawLog.SourceKind },
                { "producer", AppConst.RawLog.Producer },
                { "fileId", failure.Context.FileId },
                { "fileName", failure.Context.FileName },
                { "relativePath", failure.Context.RelativePath },
                { "folderDate", MongoDocumentValue.DateOnly(failure.Context.FolderDate) },
                { "offsetStart", failure.Context.OffsetStart },
                { "offsetEnd", failure.Context.OffsetEnd }
            } },
        { "rawPayload", new BsonDocument
            {
                { "format", AppConst.RawLog.PayloadFormat },
                { "text", failure.Context.RawPayloadText },
                { "sha256", EventIdentityFactory.ComputePayloadHash(failure.Context) }
            } },
        { "error", new BsonDocument
            {
                { "code", failure.Code },
                { "message", failure.Message },
                { "parserVersion", failure.ParserVersion }
            } },
        { "receivedAtUtc", MongoDocumentValue.DateTimeOffset(receivedAtUtc) },
        { "retryable", failure.Retryable },
        { "resolvedAtUtc", BsonNull.Value },
        { "ingestion", new BsonDocument("workerId", workerId) }
    };
}
