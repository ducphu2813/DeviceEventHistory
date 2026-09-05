using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionRecoveryStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options,
    SqlProjectionBatchOperations operations,
    TimeProvider timeProvider) : IProjectionRecoveryStore
{
    public async Task<ProjectionRecoveryDefinition> EnsureDefinitionAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        string mappingVersion,
        string ownershipVersion,
        int metricSetVersion,
        DateTimeOffset coverageStartAtUtc,
        string timeZoneId,
        bool requireExisting,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await operations.EnsureFencedAsync(session, identity, lease, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT [MappingVersion], [OwnershipVersion], [MetricSetVersion], [CoverageStartAtUtc],
                   [TimeZoneId], [LifecycleStatus]
            FROM {Table("ProjectionDefinition")} WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProjectionName] = @projectionName AND [ProjectionVersion] = @projectionVersion;
            """;
        AddIdentity(command, identity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var storedMapping = reader.GetString(0);
            var storedOwnership = reader.GetString(1);
            var storedMetricSet = reader.GetInt32(2);
            var storedCoverage = new DateTimeOffset(
                DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc));
            var storedTimeZone = reader.GetString(4);
            var status = reader.GetString(5);
            await reader.DisposeAsync();
            await session.RollbackAsync(cancellationToken);

            if (!string.Equals(storedMapping, mappingVersion, StringComparison.Ordinal) ||
                !string.Equals(storedOwnership, ownershipVersion, StringComparison.Ordinal) ||
                storedMetricSet != metricSetVersion ||
                storedCoverage != coverageStartAtUtc.ToUniversalTime() ||
                !string.Equals(storedTimeZone, timeZoneId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    StatisticsContractConstants.Messages.Format(
                        StatisticsContractConstants.Messages.MSG_RECOVERY_DEFINITION_CONFLICT,
                        identity.ProjectionName,
                        identity.ProjectionVersion));
            }

            if (!requireExisting && status == "failed")
            {
                command.CommandText = $"""
                    UPDATE {Table("ProjectionDefinition")}
                    SET [LifecycleStatus] = 'building'
                    WHERE [ProjectionName] = @projectionName
                      AND [ProjectionVersion] = @projectionVersion;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
                await session.CommitAsync(cancellationToken);
                return new ProjectionRecoveryDefinition(true, "building");
            }

            return new ProjectionRecoveryDefinition(
                requireExisting || status is not "ready" and not "active",
                status);
        }

        await reader.DisposeAsync();
        if (requireExisting)
        {
            await session.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_RECOVERY_DEFINITION_MISSING,
                    identity.ProjectionName,
                    identity.ProjectionVersion));
        }

        command.CommandText = $"""
            INSERT INTO {Table("ProjectionDefinition")}
            (
                [ProjectionName], [ProjectionVersion], [MappingVersion], [OwnershipVersion], [MetricSetVersion],
                [CoverageStartAtUtc], [TimeZoneId], [LifecycleStatus], [CreatedAtUtc]
            )
            VALUES
            (
                @projectionName, @projectionVersion, @mappingVersion, @ownershipVersion, @metricSetVersion,
                @coverageStartAtUtc, @timeZoneId, 'building', @createdAtUtc
            );
            """;
        command.Parameters.Add(new SqlParameter("@mappingVersion", mappingVersion));
        command.Parameters.Add(new SqlParameter("@ownershipVersion", ownershipVersion));
        command.Parameters.Add(new SqlParameter("@metricSetVersion", metricSetVersion));
        command.Parameters.Add(new SqlParameter("@coverageStartAtUtc", coverageStartAtUtc.UtcDateTime));
        command.Parameters.Add(new SqlParameter("@timeZoneId", timeZoneId));
        command.Parameters.Add(new SqlParameter("@createdAtUtc", timeProvider.GetUtcNow().UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
        return new ProjectionRecoveryDefinition(true, "building");
    }

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
                WHERE [ProjectionRunId] = @runId
            )
            BEGIN
                INSERT INTO {Table("ProjectionRun")}
                (
                    [ProjectionRunId], [ProjectionName], [ProjectionVersion], [RunType],
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
            WHERE [ProjectionRunId] = @runId;

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

    private static void AddIdentity(SqlCommand command, ProjectionIdentity identity)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
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

    private string Table(string name) => $"[{options.SchemaName}].[{name}]";
}
