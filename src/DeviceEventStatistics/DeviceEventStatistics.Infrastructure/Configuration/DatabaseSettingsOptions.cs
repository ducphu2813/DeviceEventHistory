namespace DeviceEventStatistics.Infrastructure.Configuration;

public sealed class DatabaseSettingsOptions
{
    public const string SectionName = "DeviceEventStatistics:DatabaseSettings";

    public MongoHistoryDatabaseOptions MongoDb { get; set; } = new();

    public SqlStatisticsDatabaseOptions SqlServer { get; set; } = new();
}

public sealed class MongoHistoryDatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string ConnectionStringEnvironmentVariable { get; set; } =
        "DEVICE_EVENT_STATISTICS_MONGO_CONNECTION_STRING";

    public string DatabaseName { get; set; } = "device_event_history";

    public string HistoryCollection { get; set; } = "device_event_history";

    public List<string> RequiredHistoryIndexNames { get; set; } =
        ["ux_event_id", "ix_statistics_persisted_event_id"];

    public void ApplyEnvironmentConnectionString()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) &&
            !string.IsNullOrWhiteSpace(ConnectionStringEnvironmentVariable))
        {
            ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty;
        }
    }
}

public sealed class SqlStatisticsDatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string ConnectionStringEnvironmentVariable { get; set; } =
        "DEVICE_EVENT_STATISTICS_SQL_CONNECTION_STRING";

    public string DatabaseName { get; set; } = "UA-REPORTING-DB";

    public string SchemaName { get; set; } = "dbo";

    public int CommandTimeoutSeconds { get; set; } = 30;

    public void ApplyEnvironmentConnectionString()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) &&
            !string.IsNullOrWhiteSpace(ConnectionStringEnvironmentVariable))
        {
            ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty;
        }
    }
}
