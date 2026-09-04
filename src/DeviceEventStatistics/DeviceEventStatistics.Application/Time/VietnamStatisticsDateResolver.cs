namespace DeviceEventStatistics.Application.Time;

public sealed class VietnamStatisticsDateResolver
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public VietnamStatisticsDateResolver(string timeZoneId = "Asia/Ho_Chi_Minh")
    {
        if (!string.Equals(timeZoneId, "Asia/Ho_Chi_Minh", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Statistics timezone must be Asia/Ho_Chi_Minh for the Sprint 3 contract.",
                nameof(timeZoneId));
        }

        TimeZoneId = timeZoneId;
    }

    public string TimeZoneId { get; }

    public StatisticsBucket Resolve(DateTimeOffset timelineAtUtc)
    {
        var localDate = DateOnly.FromDateTime(timelineAtUtc.ToOffset(VietnamOffset).DateTime);
        var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextLocalStart = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return new StatisticsBucket(
            localDate,
            new DateTimeOffset(localStart, VietnamOffset).ToUniversalTime(),
            new DateTimeOffset(nextLocalStart, VietnamOffset).ToUniversalTime(),
            TimeZoneId);
    }
}
