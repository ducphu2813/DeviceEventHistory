namespace DeviceEventHistory.Infrastructure.MongoDb.Configuration;

/// <summary>
/// Retention policy supplied by the History Worker. It is deliberately kept
/// separate from connection settings so the infrastructure layer does not
/// depend on Worker configuration types.
/// </summary>
public sealed record MongoRetentionSettings(
    int HistoryRetentionDays,
    int FailureRetentionDays);
