using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlOperationalCleanupStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options) : IOperationalCleanupStore
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
        OperationalCleanupOptions cleanup,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        // Cleanup runs only after the worker-level lease has been acquired. It does not
        // take the transactional projection writer gate: every target is aged beyond the
        // source retention window or is a completed operational artifact, so it cannot
        // participate in the active projection transaction.
        var deletedProcessedEvents = await DeleteProcessedEventBatchAsync(
            session,
            identity,
            cleanup.ProcessedEventCutoffAtUtc,
            cleanup.BatchSize,
            cancellationToken);
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
                    WHERE run.[RunId] = stage.[RunId]
                      AND run.[ProjectionName] = @projectionName
                      AND run.[ProjectionVersion] = @projectionVersion
                      AND run.[Status] IN ('succeeded', 'failed', 'cancelled')
                      AND run.[CompletedAtUtc] < @stagingCutoffAtUtc
                );
                """,
                command => AddIdentityAndCutoff(command, identity, "@stagingCutoffAtUtc", cleanup.StagingCutoffAtUtc),
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
            command => AddIdentityAndCutoff(command, identity, "@projectionRunCutoffAtUtc", cleanup.ProjectionRunCutoffAtUtc),
            cancellationToken);

        var deletedResolvedFailures = await ExecuteAsync(
            session,
            $"""
            DELETE FROM {Table("ProjectionFailure")}
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [ResolvedAtUtc] IS NOT NULL
              AND [ResolvedAtUtc] < @resolvedFailureCutoffAtUtc;
            """,
            command => AddIdentityAndCutoff(command, identity, "@resolvedFailureCutoffAtUtc", cleanup.ResolvedFailureCutoffAtUtc),
            cancellationToken);

        var deletedCompletedReconciliationRequests = await ExecuteAsync(
            session,
            $"""
            DELETE FROM {Table("ReconciliationRequest")}
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [Status] IN ('Completed', 'Cancelled')
              AND [CompletedAtUtc] < @completedReconciliationCutoffAtUtc;
            """,
            command => AddIdentityAndCutoff(command, identity, "@completedReconciliationCutoffAtUtc", cleanup.CompletedReconciliationCutoffAtUtc),
            cancellationToken);
        await session.CommitAsync(cancellationToken);
        return new OperationalCleanupResult(
            deletedProcessedEvents,
            deletedStagingRows,
            deletedRuns,
            deletedResolvedFailures,
            deletedCompletedReconciliationRequests);
    }

    private Task<int> DeleteProcessedEventBatchAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        DateTimeOffset cutoffAtUtc,
        int batchSize,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            session,
            $"""
            ;WITH candidates AS
            (
                SELECT TOP (@cleanupBatchSize) [ProcessedEventId]
                FROM {Table("ProcessedEvent")} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [SourcePersistedAtUtc] < @processedEventCutoffAtUtc
                ORDER BY [SourcePersistedAtUtc], [ProcessedEventId]
            )
            DELETE target
            FROM {Table("ProcessedEvent")} target
            INNER JOIN candidates ON candidates.[ProcessedEventId] = target.[ProcessedEventId];
            """,
            command =>
            {
                AddIdentityAndCutoff(command, identity, "@processedEventCutoffAtUtc", cutoffAtUtc);
                command.Parameters.Add(new SqlParameter("@cleanupBatchSize", batchSize));
            },
            cancellationToken);

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

    private static void AddIdentityAndCutoff(
        SqlCommand command,
        ProjectionIdentity identity,
        string cutoffParameterName,
        DateTimeOffset cutoffAtUtc)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter(cutoffParameterName, cutoffAtUtc.UtcDateTime));
    }

    private string Table(string name) =>
        StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, name);
}
