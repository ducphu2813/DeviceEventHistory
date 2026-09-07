namespace DeviceEventStatistics.Infrastructure.Configuration;

public sealed record SqlProjectionWriterOptions(
    int MaxAttempts,
    TimeSpan MinimumRetryDelay,
    TimeSpan MaximumRetryDelay);
