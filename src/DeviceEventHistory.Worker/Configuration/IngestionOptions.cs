using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Worker.Configuration;

public sealed class IngestionOptions
{
    public const string SectionName = AppConst.Configuration.IngestionSection;

    public int DefaultRetentionDays { get; set; } = AppConst.Defaults.DefaultRetentionDays;

    public int FailureRetentionDays { get; set; } = AppConst.Defaults.FailureRetentionDays;

    public int PersistenceRetryCount { get; set; } = AppConst.Defaults.PersistenceRetryCount;

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(AppConst.Defaults.ShutdownTimeoutSeconds);

    public int MaxRawPayloadBytes { get; set; } = AppConst.Defaults.MaxRawPayloadBytes;
}
