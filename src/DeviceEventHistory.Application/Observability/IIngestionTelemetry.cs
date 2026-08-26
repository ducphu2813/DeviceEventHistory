using DeviceEventHistory.Application.Parsing;

namespace DeviceEventHistory.Application.Observability;

public interface IIngestionTelemetry
{
    void RecordFilesDiscovered(string sourceId, string mode, int count);

    void RecordSourceAccessFailure(string sourceId, string mode);

    void RecordFileProcessingStarted(string sourceId, long fileId);

    void RecordFileProcessingCompleted(string sourceId, long fileId, string result);

    void RecordBytesRead(string sourceId, long fileId, long bytes);

    void RecordRecordsFramed(string sourceId, long fileId, int count);

    void RecordPartialRecord(string sourceId, long fileId, int pendingBytes);

    void RecordParseResult(string sourceId, long fileId, RawRecordParseStatus status);

    void RecordHistoryWrite(bool wasAlreadyPersisted, TimeSpan duration);

    void RecordFailureWrite(bool wasAlreadyPersisted, TimeSpan duration);

    void RecordCheckpointAdvance(string sourceId, long fileId, long position, bool succeeded);

    void RecordMongoRetry(string operation);

    void RecordMongoFailure(string operation);

    void RecordPersistenceLatency(string operation, TimeSpan duration);

    void RecordOversizedRecord(string sourceId, long fileId);

    void RecordFileTruncated(string sourceId, long fileId);

    void RecordProgress(
        string sourceId,
        long fileId,
        long checkpointPosition,
        long? fileLength,
        int pendingBytes,
        DateTimeOffset? checkpointUpdatedAtUtc);
}
