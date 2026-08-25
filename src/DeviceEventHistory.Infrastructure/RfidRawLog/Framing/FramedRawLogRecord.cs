namespace DeviceEventHistory.Infrastructure.RfidRawLog.Framing;

public sealed record FramedRawLogRecord
{
    public required long StartOffset { get; init; }

    public required long EndOffsetExclusive { get; init; }

    public required byte[] Payload { get; init; }
}
