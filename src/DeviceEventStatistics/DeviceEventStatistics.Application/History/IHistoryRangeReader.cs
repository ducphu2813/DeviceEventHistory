namespace DeviceEventStatistics.Application.History;

public interface IHistoryRangeReader
{
    Task<HistoryReadResult> ReadRangePageAsync(
        DateTimeOffset fromTimelineAtUtc,
        DateTimeOffset toTimelineAtUtc,
        SourceCursor? after,
        int pageSize,
        long? companyId = null,
        long? deviceId = null,
        CancellationToken cancellationToken = default);
}
