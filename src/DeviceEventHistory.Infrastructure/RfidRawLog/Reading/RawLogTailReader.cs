using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Reading;

public sealed class RawLogTailReader(
    int readBufferBytes,
    IEnumerable<IRawLogSourceTailReader> sourceReaders) : IRawLogTailReader
{
    private readonly IReadOnlyDictionary<RawLogSourceMode, IRawLogSourceTailReader> sourceReaders =
        sourceReaders.ToDictionary(reader => reader.Mode);

    public Task<RawLogTailReadResult> ReadAsync(
        RawLogFileDescriptor file,
        long offset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!sourceReaders.TryGetValue(file.Mode, out var sourceReader))
        {
            throw new InvalidOperationException(
                AppConst.Messages.Format(AppConst.Messages.MSG_NO_RAW_LOG_TAIL_READER, file.Mode));
        }

        return sourceReader.ReadAsync(file, offset, readBufferBytes, cancellationToken);
    }
}
