namespace DeviceEventStatistics.Application.Projection;

public interface IDurationRefreshStore
{
    Task<int> RefreshAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        DateTimeOffset asOfAtUtc,
        int maxStreams,
        CancellationToken cancellationToken = default);
}
