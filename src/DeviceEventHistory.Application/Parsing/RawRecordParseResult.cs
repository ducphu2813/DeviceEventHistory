namespace DeviceEventHistory.Application.Parsing;

public sealed record RawRecordParseResult
{
    public required RawRecordContext Context { get; init; }

    public required ParsedRfidRawRecord Parsed { get; init; }

    public required RawRecordParseStatus Status { get; init; }

    public IReadOnlyList<RawParseIssue> Issues { get; init; } = [];
}
