namespace DeviceEventStatistics.Application.Time;

public sealed record StatisticsBucket(
    DateOnly StatisticsDate,
    DateTimeOffset BucketStartAtUtc,
    DateTimeOffset BucketEndAtUtc,
    string TimeZoneId);
