using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class FileIngestionState
{
    private readonly Queue<FramedRawLogRecord> readyRecords = [];
    private int scheduleRequested;
    private int wakeRequested;

    public FileIngestionState(
        RawLogFileDescriptor descriptor,
        IngestionCheckpoint checkpoint,
        long readOffset,
        IRawLogRecordFramer framer,
        bool startupExistingFile)
    {
        Descriptor = descriptor;
        Checkpoint = checkpoint;
        ReadOffset = readOffset;
        Framer = framer;
        StartupExistingFile = startupExistingFile;
    }

    public RawLogFileDescriptor Descriptor { get; private set; }

    public IngestionCheckpoint Checkpoint { get; private set; }

    public long ReadOffset { get; private set; }

    public IRawLogRecordFramer Framer { get; }

    public bool StartupExistingFile { get; }

    public FileIngestionStateStatus Status { get; private set; } = FileIngestionStateStatus.Ready;

    public long? LastObservedFileLength { get; private set; }

    public int ReadyRecordCount => readyRecords.Count;

    public bool HasPendingBytes => Framer.PendingByteCount > 0;

    public string Key => Checkpoint.Key.DocumentId;

    public bool IsStopped => Status == FileIngestionStateStatus.Stopped;

    public void UpdateDescriptor(RawLogFileDescriptor descriptor) => Descriptor = descriptor;

    public void SetStatus(FileIngestionStateStatus status) => Status = status;

    public void SetReadOffset(long readOffset) => ReadOffset = readOffset;

    public void SetLastObservedFileLength(long fileLength) => LastObservedFileLength = fileLength;

    public void CommitCheckpoint(IngestionCheckpoint checkpoint)
    {
        Checkpoint = checkpoint;
        if (ReadOffset < checkpoint.Position)
        {
            ReadOffset = checkpoint.Position;
        }
    }

    public void EnqueueRecords(IEnumerable<FramedRawLogRecord> records)
    {
        foreach (var record in records)
        {
            readyRecords.Enqueue(record);
        }
    }

    public bool TryDequeueRecord(out FramedRawLogRecord? record)
    {
        if (readyRecords.Count == 0)
        {
            record = null;
            return false;
        }

        record = readyRecords.Dequeue();
        return true;
    }

    public void ResetToCheckpoint()
    {
        readyRecords.Clear();
        Framer.Reset();
        ReadOffset = Checkpoint.Position;
    }

    public bool TryRequestSchedule()
    {
        if (Interlocked.CompareExchange(ref scheduleRequested, 1, 0) == 0)
        {
            return true;
        }

        Interlocked.Exchange(ref wakeRequested, 1);
        return false;
    }

    public void ClearScheduleRequest() => Volatile.Write(ref scheduleRequested, 0);

    public bool ConsumeWakeRequest() => Interlocked.Exchange(ref wakeRequested, 0) == 1;
}

public enum FileIngestionStateStatus
{
    Ready = 0,
    Processing = 1,
    CaughtUp = 2,
    WaitingForMoreData = 3,
    Faulted = 4,
    Stopped = 5
}
