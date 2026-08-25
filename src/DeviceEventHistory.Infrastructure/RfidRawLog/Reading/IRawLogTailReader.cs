using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Reading;

public interface IRawLogTailReader
{
    Task<RawLogTailReadResult> ReadAsync(
        RawLogFileDescriptor file,
        long offset,
        CancellationToken cancellationToken = default);
}
