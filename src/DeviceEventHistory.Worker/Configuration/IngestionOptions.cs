namespace DeviceEventHistory.Worker.Configuration;

public sealed class IngestionOptions
{
    public const string SectionName = "DeviceEventHistory:Ingestion";

    public int DefaultRetentionDays { get; set; } = 90;

    public int FailureRetentionDays { get; set; } = 30;

    public int PersistenceRetryCount { get; set; } = 5;

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxRawPayloadBytes { get; set; } = 1024 * 1024;
}
