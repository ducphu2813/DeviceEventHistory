namespace DeviceEventHistory.Infrastructure.MongoDb.Configuration;

public sealed class MongoDbOptions
{
    public const string SectionName = "DeviceEventHistory:MongoDb";

    public string ConnectionString { get; set; } = string.Empty;

    public string ConnectionStringEnvironmentVariable { get; set; } = "DEVICE_EVENT_HISTORY_MONGODB_CONNECTION_STRING";

    public string DatabaseName { get; set; } = "device_event_history";

    public string HistoryCollection { get; set; } = "device_event_history";

    public string FailureCollection { get; set; } = "ingestion_failures";

    public string CheckpointCollection { get; set; } = "ingestion_checkpoints";

    public void ApplyEnvironmentConnectionString()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(ConnectionStringEnvironmentVariable))
        {
            ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty;
        }
    }
}
