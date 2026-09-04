namespace DeviceEventStatistics.Application.History;

public sealed record SourceCursor(DateTimeOffset PersistedAtUtc, string EventId)
{
    public static SourceCursor? From(HistoryEvent historyEvent) =>
        historyEvent.PersistedAtUtc is DateTimeOffset persistedAtUtc &&
        historyEvent.EventId is not null
            ? new SourceCursor(persistedAtUtc, historyEvent.EventId)
            : null;

    public bool IsAfter(SourceCursor other) =>
        PersistedAtUtc > other.PersistedAtUtc ||
        (PersistedAtUtc == other.PersistedAtUtc &&
         string.CompareOrdinal(EventId, other.EventId) > 0);
}
