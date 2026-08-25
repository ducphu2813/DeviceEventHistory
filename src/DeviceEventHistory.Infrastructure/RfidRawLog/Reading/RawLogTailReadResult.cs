namespace DeviceEventHistory.Infrastructure.RfidRawLog.Reading;

public sealed record RawLogTailReadResult
{
    public required long StartOffset { get; init; }

    public required long NextOffset { get; init; }

    public required long FileLength { get; init; }

    public required byte[] Data { get; init; }

    public required bool IsTruncated { get; init; }

    public bool HasMore => NextOffset < FileLength;
}
