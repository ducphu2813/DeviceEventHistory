using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Reading;

public interface IRawLogSourceTailReader
{
    RawLogSourceMode Mode { get; }

    Task<RawLogTailReadResult> ReadAsync(
        RawLogFileDescriptor file,
        long offset,
        int maxBytes,
        CancellationToken cancellationToken);
}
