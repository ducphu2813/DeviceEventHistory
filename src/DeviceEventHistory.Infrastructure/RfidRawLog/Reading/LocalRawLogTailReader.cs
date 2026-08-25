using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Reading;

public sealed class LocalRawLogTailReader : IRawLogSourceTailReader
{
    public RawLogSourceMode Mode => RawLogSourceMode.Local;

    public async Task<RawLogTailReadResult> ReadAsync(
        RawLogFileDescriptor file,
        long offset,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ValidateArguments(file, offset, maxBytes);

        await using var stream = new FileStream(
            file.Location,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous,
                BufferSize = maxBytes
            });

        var fileLength = stream.Length;
        if (offset > fileLength)
        {
            return new RawLogTailReadResult
            {
                StartOffset = offset,
                NextOffset = offset,
                FileLength = fileLength,
                Data = [],
                IsTruncated = true
            };
        }

        stream.Position = offset;
        var buffer = new byte[maxBytes];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
        if (bytesRead != buffer.Length)
        {
            Array.Resize(ref buffer, bytesRead);
        }

        fileLength = stream.Length;
        return new RawLogTailReadResult
        {
            StartOffset = offset,
            NextOffset = offset + bytesRead,
            FileLength = fileLength,
            Data = buffer,
            IsTruncated = false
        };
    }

    private static void ValidateArguments(RawLogFileDescriptor file, long offset, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
    }
}
