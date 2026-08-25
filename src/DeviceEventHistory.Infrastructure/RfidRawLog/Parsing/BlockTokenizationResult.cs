using DeviceEventHistory.Application.Parsing;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;

public sealed record BlockTokenizationResult
{
    public IReadOnlyList<RawBlockToken> Blocks { get; init; } = [];

    public IReadOnlyList<RawParseIssue> Issues { get; init; } = [];
}
