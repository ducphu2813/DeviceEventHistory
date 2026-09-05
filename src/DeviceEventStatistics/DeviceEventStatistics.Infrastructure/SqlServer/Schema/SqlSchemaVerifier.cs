using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Domain.Common;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Schema;

public sealed class SqlSchemaVerifier(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options)
{
    public const string ExpectedLatestMigrationId = "011_AddScopedProcessedEventContract";

    private static readonly string[] RequiredProcessedEventColumns =
    [
        "CompanyId",
        "DeviceId"
    ];

    private static readonly string[] RequiredCheckpointColumns =
    [
        "LastCompletedSweepAtUtc",
        "AuditLastSourceDocumentId",
        "AuditStartedAtUtc",
        "AuditCompletedAtUtc",
        "AuditCycle"
    ];

    private static readonly string[] RequiredTables =
    [
        StatisticsSqlObjectNames.Table("SchemaMigration"),
        StatisticsSqlObjectNames.Table("ProjectionDefinition"),
        StatisticsSqlObjectNames.Table("MetricDefinition"),
        StatisticsSqlObjectNames.Table("DeviceDimension"),
        StatisticsSqlObjectNames.Table("DeviceEventDaily"),
        StatisticsSqlObjectNames.Table("DeviceDailySnapshot"),
        StatisticsSqlObjectNames.Table("DeviceStateDaily"),
        StatisticsSqlObjectNames.Table("DeviceStateCursor"),
        StatisticsSqlObjectNames.Table("ProcessedEvent"),
        StatisticsSqlObjectNames.Table("ProjectionCheckpoint"),
        StatisticsSqlObjectNames.Table("ProjectionCoverage"),
        StatisticsSqlObjectNames.Table("ReconciliationRequest"),
        StatisticsSqlObjectNames.Table("ProjectionFailure"),
        StatisticsSqlObjectNames.Table("ProjectionRun"),
        StatisticsSqlObjectNames.Table("IngestionQualityDaily"),
        StatisticsSqlObjectNames.Table("ProjectionStagingEvent"),
        StatisticsSqlObjectNames.Table("ProjectionStagingDaily"),
        StatisticsSqlObjectNames.Table("ProjectionStagingState"),
        StatisticsSqlObjectNames.Table("ProjectionStagingSummary"),
        StatisticsSqlObjectNames.Table("ProjectionStagingCoverage"),
        StatisticsSqlObjectNames.Table("ProjectionStagingQuality"),
        StatisticsSqlObjectNames.Table("ProjectionStagingCursor")
    ];

    private static readonly string[] RequiredTableTypes =
    [
        "ProjectionProcessedEventType",
        "ProjectionProcessedEventTypeV2",
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
        await VerifyCheckpointColumnsAsync(connection, cancellationToken);
        await VerifyProcessedEventColumnsAsync(connection, cancellationToken);
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
        command.CommandText = $"""
            SELECT [Checksum]
            FROM {StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, "SchemaMigration")}
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

    private async Task VerifyProcessedEventColumnsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [name]
            FROM sys.columns
            WHERE [object_id] = OBJECT_ID(@tableName);
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter(
            "@tableName",
            StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, "ProcessedEvent")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken)) actualColumns.Add(reader.GetString(0));

        var missingColumns = RequiredProcessedEventColumns
            .Where(column => !actualColumns.Contains(column))
            .ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_COLUMNS_MISSING,
                    StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, "ProcessedEvent"),
                    string.Join(", ", missingColumns)));
        }
    }

    private async Task VerifyCheckpointColumnsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [name]
            FROM sys.columns
            WHERE [object_id] = OBJECT_ID(@tableName);
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter(
            "@tableName",
            StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, "ProjectionCheckpoint")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken)) actualColumns.Add(reader.GetString(0));

        var missingColumns = RequiredCheckpointColumns
            .Where(column => !actualColumns.Contains(column))
            .ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_COLUMNS_MISSING,
                    StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, "ProjectionCheckpoint"),
                    string.Join(", ", missingColumns)));
        }
    }

    private async Task VerifyMetricRegistryAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT_BIG(*)
            FROM {StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, "MetricDefinition")}
            WHERE [MetricSetVersion] = 1;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_MISSING);
        }
    }

}
