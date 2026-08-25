namespace DeviceEventHistory.Infrastructure.RfidRawLog.Framing;

public interface IRawLogRecordFramer
{
    int PendingByteCount { get; }

    IReadOnlyList<FramedRawLogRecord> Append(ReadOnlyMemory<byte> data, long startOffset);

    void Reset();
}
