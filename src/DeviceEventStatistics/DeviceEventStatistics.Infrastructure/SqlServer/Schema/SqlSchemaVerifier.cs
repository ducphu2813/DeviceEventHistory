using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Domain.Common;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Schema;

public sealed class SqlSchemaVerifier(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options)
{
    public const string ExpectedLatestMigrationId = "006_CreateStatePersistenceContracts";

    private static readonly string[] RequiredTables =
    [
        "SchemaMigration",
        "ProjectionDefinition",
        "MetricDefinition",
        "DeviceDimension",
        "DeviceEventDaily",
        "DeviceDailySnapshot",
        "DeviceStateDaily",
        "DeviceStateCursor",
        "ProcessedEvent",
        "ProjectionCheckpoint",
        "ProjectionCoverage",
        "ReconciliationRequest",
        "ProjectionFailure",
        "ProjectionRun",
        "IngestionQualityDaily",
        "ProjectionStagingEvent",
        "ProjectionStagingDaily",
        "ProjectionStagingState"
    ];

    private static readonly string[] RequiredTableTypes =
    [
        "ProjectionProcessedEventType",
        "ProjectionMetricContributionType",
        "ProjectionDeviceSummaryType",
        "ProjectionStateObservationType",
        "ProjectionStateDailyType",
        "ProjectionStateCursorType",
        "ProjectionReconciliationRequestType",
        "ProjectionQualityContributionType",
        "ProjectionFailureType"
    ];

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await VerifySchemaExistsAsync(connection, cancellationToken);
        await VerifyMigrationAsync(connection, cancellationToken);
        await VerifyTablesAsync(connection, cancellationToken);
        await VerifyTableTypesAsync(connection, cancellationToken);
        await VerifyMetricRegistryAsync(connection, cancellationToken);
    }

    private async Task VerifySchemaExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SCHEMA_ID(@schemaName);";
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@schemaName", options.SchemaName));
        if (await command.ExecuteScalarAsync(cancellationToken) is null or DBNull)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_SCHEMA_MISSING_WITHOUT_DATABASE,
                    options.SchemaName));
        }
    }

    private async Task VerifyMigrationAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [Checksum]
            FROM [device_stats].[SchemaMigration]
            WHERE [MigrationId] = @migrationId;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@migrationId", ExpectedLatestMigrationId));
        var checksum = await command.ExecuteScalarAsync(cancellationToken);
        if (checksum is null or DBNull)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_MIGRATION_MISSING,
                    ExpectedLatestMigrationId));
        }
    }

    private async Task VerifyTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [name]
            FROM sys.tables
            WHERE [schema_id] = SCHEMA_ID(@schemaName);
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@schemaName", options.SchemaName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var actualTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken)) actualTables.Add(reader.GetString(0));

        var missingTables = RequiredTables.Where(table => !actualTables.Contains(table)).ToArray();
        if (missingTables.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_TABLES_MISSING,
                    string.Join(", ", missingTables)));
        }
    }

    private async Task VerifyTableTypesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [name]
            FROM sys.table_types
            WHERE [schema_id] = SCHEMA_ID(@schemaName);
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@schemaName", options.SchemaName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var actualTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken)) actualTypes.Add(reader.GetString(0));

        var missingTypes = RequiredTableTypes.Where(type => !actualTypes.Contains(type)).ToArray();
        if (missingTypes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_TYPES_MISSING,
                    string.Join(", ", missingTypes)));
        }
    }

    private async Task VerifyMetricRegistryAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT_BIG(*)
            FROM {QuoteIdentifier(options.SchemaName)}.[MetricDefinition]
            WHERE [MetricSetVersion] = 1;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_MISSING);
        }
    }

    private static string QuoteIdentifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
}
