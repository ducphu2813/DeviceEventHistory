using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.MongoDb.Configuration;

public sealed class MongoDbOptions
{
    public const string SectionName = AppConst.Configuration.MongoDbSection;

    public string ConnectionString { get; set; } = string.Empty;

    public string ConnectionStringEnvironmentVariable { get; set; } = AppConst.EnvironmentVariables.MongoDbConnectionString;

    public string DatabaseName { get; set; } = AppConst.MongoDb.DefaultDatabaseName;

    public string HistoryCollection { get; set; } = AppConst.MongoDb.HistoryCollection;

    public string FailureCollection { get; set; } = AppConst.MongoDb.FailureCollection;

    public string CheckpointCollection { get; set; } = AppConst.MongoDb.CheckpointCollection;

    public void ApplyEnvironmentConnectionString()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(ConnectionStringEnvironmentVariable))
        {
            ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty;
        }
    }
}
