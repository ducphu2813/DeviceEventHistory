namespace DeviceEventStatistics.Application.History;

public interface IHistoryEventReader
{
    Task<HistoryReadResult> ReadPageAsync(
        DateTimeOffset fromPersistedAtUtc,
        DateTimeOffset toPersistedAtUtc,
        SourceCursor? after,
        int pageSize,
        IReadOnlyCollection<long>? companyIds = null,
        IReadOnlyCollection<long>? deviceIds = null,
        CancellationToken cancellationToken = default);
}
