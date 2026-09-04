namespace DeviceEventStatistics.Application.History;

public sealed record HistoryReadResult(
    IReadOnlyList<HistoryEvent> Events,
    SourceCursor? NextCursor,
    DateTimeOffset UpperBoundAtUtc,
    bool IsCaughtUp);

public sealed record HistoryAuditResult(
    IReadOnlyList<HistoryEvent> Events,
    string? NextSourceDocumentId,
    bool IsComplete);
