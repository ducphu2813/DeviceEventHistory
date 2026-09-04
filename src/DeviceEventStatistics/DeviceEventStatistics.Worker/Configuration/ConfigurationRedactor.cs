using DeviceEventStatistics.Infrastructure.Configuration;

namespace DeviceEventStatistics.Worker.Configuration;

public sealed record RedactedStatisticsConfigurationSummary(
    bool Enabled,
    string WorkerId,
    ProjectionMode ProjectionMode,
    string ProjectionName,
    int ProjectionVersion,
    bool MongoConnectionStringConfigured,
    string MongoDatabaseName,
    string MongoHistoryCollection,
    bool SqlConnectionStringConfigured,
    string SqlDatabaseName,
    string SqlSchemaName,
    IReadOnlyCollection<long> CompanyIds,
    IReadOnlyCollection<long> DeviceIds);

public sealed class ConfigurationRedactor
{
    public RedactedStatisticsConfigurationSummary CreateSummary(
        WorkerOptions worker,
        ProjectionOptions projection,
        DatabaseSettingsOptions database)
    {
        var scope = projection.Scope ?? new ProjectionScopeOptions();
        var mongo = database.MongoDb ?? new MongoHistoryDatabaseOptions();
        var sql = database.SqlServer ?? new SqlStatisticsDatabaseOptions();

        return new RedactedStatisticsConfigurationSummary(
            worker.Enabled,
            worker.WorkerId.Trim(),
            projection.Mode,
            projection.Name.Trim(),
            projection.ProjectionVersion,
            !string.IsNullOrWhiteSpace(mongo.ConnectionString),
            mongo.DatabaseName,
            mongo.HistoryCollection,
            !string.IsNullOrWhiteSpace(sql.ConnectionString),
            sql.DatabaseName,
            sql.SchemaName,
            scope.CompanyIds,
            scope.DeviceIds);
    }
}
