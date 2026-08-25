namespace DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;

public sealed record RawBlockToken
{
    public required string Name { get; init; }

    public required string Arguments { get; init; }

    public required string RawText { get; init; }
}
