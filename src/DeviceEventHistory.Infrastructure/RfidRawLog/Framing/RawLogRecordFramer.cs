using System.Text;

using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Framing;

public sealed class RawLogRecordFramer : IRawLogRecordFramer
{
    private static readonly byte[] RecordTerminator = Encoding.UTF8.GetBytes(AppConst.RawLog.RecordTerminator);
    private readonly int maxRecordBytes;
    private readonly List<byte> pending = [];
    private long pendingStartOffset;
    private bool hasPendingStartOffset;

    public RawLogRecordFramer(int maxRecordBytes)
    {
        this.maxRecordBytes = maxRecordBytes;
    }

    public int PendingByteCount => pending.Count;

    public IReadOnlyList<FramedRawLogRecord> Append(ReadOnlyMemory<byte> data, long startOffset)
    {
        if (data.Length == 0)
        {
            return [];
        }

        if (!hasPendingStartOffset)
        {
            pendingStartOffset = startOffset;
            hasPendingStartOffset = true;
        }
        else if (pendingStartOffset + pending.Count != startOffset)
        {
            throw new ArgumentException(
                AppConst.Messages.MSG_RAW_LOG_CHUNK_NOT_CONTIGUOUS,
                nameof(startOffset));
        }

        pending.AddRange(data.ToArray());
        TrimLeadingLineBreaks();
        if (pending.Count == 0)
        {
            hasPendingStartOffset = false;
            return [];
        }

        var records = new List<FramedRawLogRecord>();

        while (true)
        {
            var terminatorIndex = FindTerminator();
            if (terminatorIndex < 0)
            {
                break;
            }

            var recordLength = terminatorIndex + RecordTerminator.Length;
            if (recordLength < pending.Count && pending[recordLength] == (byte)'\r')
            {
                recordLength++;
                if (recordLength < pending.Count && pending[recordLength] == (byte)'\n')
                {
                    recordLength++;
                }
            }
            else if (recordLength < pending.Count && pending[recordLength] == (byte)'\n')
            {
                recordLength++;
            }

            records.Add(new FramedRawLogRecord
            {
                StartOffset = pendingStartOffset,
                EndOffsetExclusive = pendingStartOffset + recordLength,
                Payload = pending.Take(recordLength).ToArray()
            });

            pending.RemoveRange(0, recordLength);
            pendingStartOffset += recordLength;
            TrimLeadingLineBreaks();
        }

        if (pending.Count > maxRecordBytes)
        {
            throw new RawLogRecordTooLargeException(maxRecordBytes);
        }

        if (pending.Count == 0)
        {
            hasPendingStartOffset = false;
        }

        return records;
    }

    public void Reset()
    {
        pending.Clear();
        pendingStartOffset = 0;
        hasPendingStartOffset = false;
    }

    private int FindTerminator()
    {
        if (pending.Count < RecordTerminator.Length)
        {
            return -1;
        }

        for (var index = 0; index <= pending.Count - RecordTerminator.Length; index++)
        {
            var matches = true;
            for (var terminatorIndex = 0; terminatorIndex < RecordTerminator.Length; terminatorIndex++)
            {
                if (pending[index + terminatorIndex] != RecordTerminator[terminatorIndex])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return index;
            }
        }

        return -1;
    }

    private void TrimLeadingLineBreaks()
    {
        var trimCount = 0;
        while (trimCount < pending.Count && (pending[trimCount] == (byte)'\r' || pending[trimCount] == (byte)'\n'))
        {
            trimCount++;
        }

        if (trimCount > 0)
        {
            pending.RemoveRange(0, trimCount);
            pendingStartOffset += trimCount;
        }
    }
}
