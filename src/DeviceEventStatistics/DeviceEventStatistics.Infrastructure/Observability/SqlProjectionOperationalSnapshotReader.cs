using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Infrastructure.MongoDb;
using DeviceEventStatistics.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.Observability;

public sealed class SqlProjectionOperationalSnapshotReader(
    SqlStatisticsDbContext sqlContext,
    MongoHistoryDbContext mongoContext,
    SqlStatisticsDatabaseOptions sqlOptions) : IProjectionOperationalSnapshotReader
{
    public Task<ProjectionOperationalSnapshot> ReadAsync(
        ProjectionIdentity identity,
        string owner,
        CancellationToken cancellationToken = default) =>
        ReadAsync(identity, owner, null, cancellationToken);

    public async Task<ProjectionOperationalSnapshot> ReadAsync(
        ProjectionIdentity identity,
        string owner,
        DateTimeOffset? retentionBoundaryAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var bounds = await mongoContext.ReadPersistedBoundsAsync(cancellationToken);
        await using var connection = sqlContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = sqlOptions.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT
                [projection_checkpoint].[LastPersistedAtUtc],
                [projection_checkpoint].[AuditStartedAtUtc],
                [projection_checkpoint].[AuditCompletedAtUtc],
                CASE WHEN [projection_checkpoint].[LeaseOwner] = @owner
                          AND [projection_checkpoint].[LeaseExpiresAtUtc] > SYSUTCDATETIME()
                     THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
                pending.[PendingCount],
                pending.[OldestRequestedAtUtc],
                pending.[OldestRequiredFromAtUtc],
                state_daily.[LastCalculatedAtUtc],
                successful_run.[LastSuccessfulRunAtUtc],
                coverage.[UnrecoverableCount],
                coverage.[GapCount]
            FROM {Table("ProjectionCheckpoint")} AS [projection_checkpoint]
            OUTER APPLY
            (
                SELECT
                    COUNT_BIG(*) AS [PendingCount],
                    MIN([RequestedAtUtc]) AS [OldestRequestedAtUtc],
                    MIN(CONVERT(datetime2(7), [FromStatisticsDate])) AS [OldestRequiredFromAtUtc]
                FROM {Table("ReconciliationRequest")}
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [Status] IN ('Pending', 'Processing')
            ) AS pending
            OUTER APPLY
            (
                SELECT MAX([CalculatedAtUtc]) AS [LastCalculatedAtUtc]
                FROM {Table("DeviceStateDaily")}
                WHERE [ProjectionVersion] = @projectionVersion
            ) AS state_daily
            OUTER APPLY
            (
                SELECT MAX([CompletedAtUtc]) AS [LastSuccessfulRunAtUtc]
                FROM {Table("ProjectionRun")}
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [Status] = 'succeeded'
            ) AS successful_run
            OUTER APPLY
            (
                SELECT
                    COUNT_BIG(CASE WHEN [CoverageStatus] = 'unrecoverable' THEN 1 END) AS [UnrecoverableCount],
                    COUNT_BIG(CASE WHEN [CoverageStatus] IN ('partial', 'unrecoverable') THEN 1 END) AS [GapCount]
                FROM {Table("ProjectionCoverage")}
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [CoverageStatus] = 'unrecoverable'
            ) AS coverage
            WHERE [projection_checkpoint].[ProjectionName] = @projectionName
              AND [projection_checkpoint].[ProjectionVersion] = @projectionVersion
              AND [projection_checkpoint].[PartitionKey] = @partitionKey;
            """;
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@partitionKey", identity.PartitionKey));
        command.Parameters.Add(new SqlParameter("@owner", owner));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ProjectionOperationalSnapshot(
                bounds.LatestPersistedAtUtc,
                bounds.OldestPersistedAtUtc,
                null,
                false,
                0,
                null,
                null,
                null,
                false,
                null,
                retentionBoundaryAtUtc);
        }

        return new ProjectionOperationalSnapshot(
            bounds.LatestPersistedAtUtc,
            bounds.OldestPersistedAtUtc,
            ReadDateTimeOffset(reader, 0),
            reader.GetBoolean(3),
            checked((int)reader.GetInt64(4)),
            ReadDateTimeOffset(reader, 5),
            ReadDateTimeOffset(reader, 7),
            ReadDateTimeOffset(reader, 8),
            !reader.IsDBNull(9) && reader.GetInt64(9) > 0,
            ReadDateTimeOffset(reader, 6),
            retentionBoundaryAtUtc,
            ReadDateTimeOffset(reader, 1),
            ReadDateTimeOffset(reader, 2),
            reader.IsDBNull(10) ? 0 : checked((int)reader.GetInt64(10)));
    }

    private string Table(string tableName) =>
        StatisticsSqlObjectNames.QualifiedTable(sqlOptions.SchemaName, tableName);

    private static DateTimeOffset? ReadDateTimeOffset(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}
