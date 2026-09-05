using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionRecoveryStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options,
    SqlProjectionBatchOperations operations) : IProjectionRecoveryStore
{
    public async Task StartRunAsync(
        ProjectionRecoveryRun run,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await operations.EnsureFencedAsync(session, lease.Identity, lease, cancellationToken);
        await ExecuteAsync(
            session,
            $"""
            IF NOT EXISTS
            (
                SELECT 1 FROM {Table("ProjectionRun")}
                WHERE [RunId] = @runId
            )
            BEGIN
                INSERT INTO {Table("ProjectionRun")}
                (
                    [RunId], [ProjectionName], [ProjectionVersion], [RunType],
                    [RequestedFromDate], [RequestedToDate], [RequestedCompanyId], [StartedAtUtc],
                    [Status], [ReadEventCount], [AggregatedEventCount], [DuplicateEventCount],
                    [IgnoredEventCount], [FailureEventCount], [AffectedRowCount]
                )
                VALUES
                (
                    @runId, @projectionName, @projectionVersion, @runType,
                    @fromDate, @toDate, @companyId, @startedAtUtc,
                    @status, 0, 0, 0, 0, 0, 0
                );
            END;
            """,
            command =>
            {
                AddRunParameters(command, run);
                command.Parameters.Add(new SqlParameter("@status", ProjectionRunStatuses.Running));
            },
            cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        ProjectionRecoveryRun run,
        ProjectionLeaseToken lease,
        string status,
        long readEventCount,
        long affectedRowCount,
        string? errorSummary = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await operations.EnsureFencedAsync(session, lease.Identity, lease, cancellationToken);
        await ExecuteAsync(
            session,
            $"""
            UPDATE {Table("ProjectionRun")}
            SET [CompletedAtUtc] = SYSUTCDATETIME(), [Status] = @status,
                [ReadEventCount] = @readEventCount, [AffectedRowCount] = @affectedRowCount,
                [ErrorSummary] = @errorSummary
            WHERE [RunId] = @runId;

            IF @runType IN ('bootstrap', 'rebuild') AND @status IN (@succeeded, @failed)
            BEGIN
                UPDATE {Table("ProjectionDefinition")}
                SET [LifecycleStatus] = CASE WHEN @status = @succeeded THEN 'ready' ELSE 'failed' END
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [LifecycleStatus] = 'building';
            END;
            """,
            command =>
            {
                AddRunParameters(command, run);
                command.Parameters.Add(new SqlParameter("@status", status));
                command.Parameters.Add(new SqlParameter("@readEventCount", readEventCount));
                command.Parameters.Add(new SqlParameter("@affectedRowCount", affectedRowCount));
                command.Parameters.Add(new SqlParameter("@errorSummary", (object?)errorSummary ?? DBNull.Value));
                command.Parameters.Add(new SqlParameter("@succeeded", ProjectionRunStatuses.Succeeded));
                command.Parameters.Add(new SqlParameter("@failed", ProjectionRunStatuses.Failed));
            },
            cancellationToken);
        await session.CommitAsync(cancellationToken);
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

    private static void AddRunParameters(SqlCommand command, ProjectionRecoveryRun run)
    {
        command.Parameters.Add(new SqlParameter("@runId", run.RunId));
        command.Parameters.Add(new SqlParameter("@projectionName", run.Identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", run.Identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@runType", run.RunType));
        command.Parameters.Add(new SqlParameter("@fromDate", run.FromStatisticsDate.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new SqlParameter("@toDate", run.ToStatisticsDate.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new SqlParameter("@companyId", (object?)run.CompanyId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@startedAtUtc", run.StartedAtUtc.UtcDateTime));
    }

    private string Table(string name) =>
        StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, name);
}
