using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlOperationalCleanupStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options,
    SqlProjectionBatchOperations operations) : IOperationalCleanupStore
{
    private static readonly string[] StagingTables =
    [
        "ProjectionStagingEvent",
        "ProjectionStagingDaily",
        "ProjectionStagingSummary",
        "ProjectionStagingState",
        "ProjectionStagingCoverage",
        "ProjectionStagingQuality",
        "ProjectionStagingCursor"
    ];

    public async Task<OperationalCleanupResult> CleanupAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        DateTimeOffset projectionRunCutoffAtUtc,
        DateTimeOffset stagingCutoffAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await operations.EnsureFencedAsync(session, identity, lease, cancellationToken);
        var deletedStagingRows = 0;
        foreach (var table in StagingTables)
        {
            deletedStagingRows += await ExecuteAsync(
                session,
                $"""
                DELETE stage
                FROM {Table(table)} stage
                WHERE EXISTS
                (
                    SELECT 1 FROM {Table("ProjectionRun")} run
                    WHERE run.[ProjectionRunId] = stage.[RunId]
                      AND run.[ProjectionName] = @projectionName
                      AND run.[ProjectionVersion] = @projectionVersion
                      AND run.[Status] IN ('succeeded', 'failed', 'cancelled')
                      AND run.[CompletedAtUtc] < @stagingCutoffAtUtc
                );
                """,
                command => AddParameters(command, identity, stagingCutoffAtUtc),
                cancellationToken);
        }

        var deletedRuns = await ExecuteAsync(
            session,
            $"""
            DELETE FROM {Table("ProjectionRun")}
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [Status] IN ('succeeded', 'cancelled')
              AND [CompletedAtUtc] < @projectionRunCutoffAtUtc;
            """,
            command =>
            {
                AddParameters(command, identity, stagingCutoffAtUtc);
                command.Parameters.Add(new SqlParameter("@projectionRunCutoffAtUtc", projectionRunCutoffAtUtc.UtcDateTime));
            },
            cancellationToken);
        await session.CommitAsync(cancellationToken);
        return new OperationalCleanupResult(deletedStagingRows, deletedRuns);
    }

    private async Task<int> ExecuteAsync(
        SqlProjectionSession session,
        string sql,
        Action<SqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = sql;
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(
        SqlCommand command,
        ProjectionIdentity identity,
        DateTimeOffset stagingCutoffAtUtc)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@stagingCutoffAtUtc", stagingCutoffAtUtc.UtcDateTime));
    }

    private string Table(string name) => $"[{options.SchemaName}].[{name}]";
}
