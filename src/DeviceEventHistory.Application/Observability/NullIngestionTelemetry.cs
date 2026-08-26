using DeviceEventHistory.Application.Parsing;

namespace DeviceEventHistory.Application.Observability;

public sealed class NullIngestionTelemetry : IIngestionTelemetry
{
    public static NullIngestionTelemetry Instance { get; } = new();

    private NullIngestionTelemetry()
    {
    }

    public void RecordFilesDiscovered(string sourceId, string mode, int count)
    {
    }

    public void RecordSourceAccessFailure(string sourceId, string mode)
    {
    }

    public void RecordFileProcessingStarted(string sourceId, long fileId)
    {
    }

    public void RecordFileProcessingCompleted(string sourceId, long fileId, string result)
    {
    }

    public void RecordBytesRead(string sourceId, long fileId, long bytes)
    {
    }

    public void RecordRecordsFramed(string sourceId, long fileId, int count)
    {
    }

    public void RecordPartialRecord(string sourceId, long fileId, int pendingBytes)
    {
    }

    public void RecordParseResult(string sourceId, long fileId, RawRecordParseStatus status)
    {
    }

    public void RecordHistoryWrite(bool wasAlreadyPersisted, TimeSpan duration)
    {
    }

    public void RecordFailureWrite(bool wasAlreadyPersisted, TimeSpan duration)
    {
    }

    public void RecordCheckpointAdvance(string sourceId, long fileId, long position, bool succeeded)
    {
    }

    public void RecordMongoRetry(string operation)
    {
    }

    public void RecordMongoFailure(string operation)
    {
    }

    public void RecordPersistenceLatency(string operation, TimeSpan duration)
    {
    }

    public void RecordOversizedRecord(string sourceId, long fileId)
    {
    }

    public void RecordFileTruncated(string sourceId, long fileId)
    {
    }

    public void RecordProgress(
        string sourceId,
        long fileId,
        long checkpointPosition,
        long? fileLength,
        int pendingBytes,
        DateTimeOffset? checkpointUpdatedAtUtc)
    {
    }
}
