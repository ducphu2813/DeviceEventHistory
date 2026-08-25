namespace DeviceEventHistory.Application.Parsing;

public sealed record RawParseIssue(
    string Code,
    string Message,
    bool IsWarning);
