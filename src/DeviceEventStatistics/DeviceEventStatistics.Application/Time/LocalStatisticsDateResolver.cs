using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Time;

public sealed class LocalStatisticsDateResolver
{
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(7);

    public LocalStatisticsDateResolver(string timeZoneId = "Asia/Ho_Chi_Minh")
    {
        if (!string.Equals(timeZoneId, "Asia/Ho_Chi_Minh", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_TIMEZONE_INVALID,
                nameof(timeZoneId));
        }

        TimeZoneId = timeZoneId;
    }

    public string TimeZoneId { get; }

    public StatisticsBucket Resolve(DateTimeOffset timelineAtUtc)
    {
        var localDate = DateOnly.FromDateTime(timelineAtUtc.ToOffset(LocalOffset).DateTime);
        var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextLocalStart = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return new StatisticsBucket(
            localDate,
            new DateTimeOffset(localStart, LocalOffset).ToUniversalTime(),
            new DateTimeOffset(nextLocalStart, LocalOffset).ToUniversalTime(),
            TimeZoneId);
    }
}
