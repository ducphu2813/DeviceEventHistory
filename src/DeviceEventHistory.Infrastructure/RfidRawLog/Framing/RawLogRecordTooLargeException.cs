using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Framing;

public sealed class RawLogRecordTooLargeException(int maxRecordBytes)
    : InvalidOperationException(
        AppConst.Messages.Format(AppConst.Messages.MSG_RAW_LOG_RECORD_TOO_LARGE, maxRecordBytes))
{
    public int MaxRecordBytes { get; } = maxRecordBytes;
}
