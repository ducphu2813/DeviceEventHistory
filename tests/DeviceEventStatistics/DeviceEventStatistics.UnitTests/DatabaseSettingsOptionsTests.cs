using DeviceEventStatistics.Infrastructure.Configuration;

namespace DeviceEventStatistics.UnitTests;

public sealed class DatabaseSettingsOptionsTests
{
    [Fact]
    public void Mongo_connection_string_can_be_loaded_from_environment()
    {
        const string variableName = "DEVICE_EVENT_STATISTICS_TEST_MONGO_CONNECTION";
        const string connectionString = "mongodb://localhost:27017";
        var previousValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, connectionString);
            var options = new MongoHistoryDatabaseOptions
            {
                ConnectionStringEnvironmentVariable = variableName
            };

            options.ApplyEnvironmentConnectionString();

            Assert.Equal(connectionString, options.ConnectionString);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }

    [Fact]
    public void Explicit_connection_string_wins_over_environment()
    {
        const string variableName = "DEVICE_EVENT_STATISTICS_TEST_SQL_CONNECTION";
        const string explicitConnectionString = "Server=explicit;Database=device_event_statistics;";
        const string environmentConnectionString = "Server=environment;Database=device_event_statistics;";
        var previousValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, environmentConnectionString);
            var options = new SqlStatisticsDatabaseOptions
            {
                ConnectionString = explicitConnectionString,
                ConnectionStringEnvironmentVariable = variableName
            };

            options.ApplyEnvironmentConnectionString();

            Assert.Equal(explicitConnectionString, options.ConnectionString);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }
}
