using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionDefinitionStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options,
    TimeProvider timeProvider) : IProjectionDefinitionStore
{
    public async Task<ResolvedProjectionDefinition?> ResolveOrCreateAsync(
        ProjectionDefinitionResolutionRequest request,
        string lifecycleStatus,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await AcquireDefinitionLockAsync(session, request.Identity, cancellationToken);

        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT [MappingVersion], [OwnershipVersion], [MetricSetVersion], [CoverageStartAtUtc],
                   [TimeZoneId], [LifecycleStatus]
            FROM {Table("ProjectionDefinition")}
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion;
            """;
        AddIdentityParameters(command, request.Identity);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        ResolvedProjectionDefinition? stored = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (stored is not null)
            {
                throw new InvalidOperationException(
                    StatisticsContractConstants.Messages.Format(
                        StatisticsContractConstants.Messages.MSG_RECOVERY_DEFINITION_CONFLICT,
                        request.Identity.ProjectionName,
                        request.Identity.ProjectionVersion));
            }

            stored = ReadDefinition(request.Identity, reader);
        }

        await reader.DisposeAsync();
        if (stored is not null)
        {
            if (request.RequiresBuildLifecycle &&
                string.Equals(stored.LifecycleStatus, ProjectionLifecycleStatuses.Failed, StringComparison.Ordinal))
            {
                command.CommandText = $"""
                    UPDATE {Table("ProjectionDefinition")}
                    SET [LifecycleStatus] = @lifecycleStatus
                    WHERE [ProjectionName] = @projectionName
                      AND [ProjectionVersion] = @projectionVersion;
                    """;
                command.Parameters.Clear();
                AddIdentityParameters(command, request.Identity);
                command.Parameters.Add(new SqlParameter("@lifecycleStatus", lifecycleStatus));
                await command.ExecuteNonQueryAsync(cancellationToken);
                stored = stored with { LifecycleStatus = lifecycleStatus };
            }

            await session.CommitAsync(cancellationToken);
            return stored;
        }

        if (request.ResumeFromStoredDefinition)
        {
            await session.RollbackAsync(cancellationToken);
            return null;
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
                @coverageStartAtUtc, @timeZoneId, @lifecycleStatus, @createdAtUtc
            );
            """;
        command.Parameters.Clear();
        AddIdentityParameters(command, request.Identity);
        command.Parameters.Add(new SqlParameter("@mappingVersion", request.MappingVersion));
        command.Parameters.Add(new SqlParameter("@ownershipVersion", request.OwnershipVersion));
        command.Parameters.Add(new SqlParameter("@metricSetVersion", request.MetricSetVersion));
        command.Parameters.Add(new SqlParameter(
            "@coverageStartAtUtc",
            request.CoverageStartAtUtc!.Value.UtcDateTime));
        command.Parameters.Add(new SqlParameter("@timeZoneId", request.TimeZoneId));
        command.Parameters.Add(new SqlParameter("@lifecycleStatus", lifecycleStatus));
        command.Parameters.Add(new SqlParameter("@createdAtUtc", timeProvider.GetUtcNow().UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);

        return new ResolvedProjectionDefinition(
            request.Identity,
            request.MappingVersion,
            request.OwnershipVersion,
            request.MetricSetVersion,
            request.CoverageStartAtUtc.Value,
            request.TimeZoneId,
            lifecycleStatus,
            true);
    }

    private async Task AcquireDefinitionLockAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @lockTimeout;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter(
            "@resource",
            $"DeviceEventStatistics:ProjectionDefinition:{identity.ProjectionName}:{identity.ProjectionVersion}"));
        command.Parameters.Add(new SqlParameter("@lockTimeout", options.CommandTimeoutSeconds * 1000));
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_PROJECTION_DEFINITION_LOCK_UNAVAILABLE);
        }
    }

    private static ResolvedProjectionDefinition ReadDefinition(
        ProjectionIdentity identity,
        SqlDataReader reader)
    {
        if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) ||
            reader.IsDBNull(3) || reader.IsDBNull(4) || reader.IsDBNull(5))
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_RECOVERY_DEFINITION_CONFLICT,
                    identity.ProjectionName,
                    identity.ProjectionVersion));
        }

        return new ResolvedProjectionDefinition(
            identity,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)),
            reader.GetString(4),
            reader.GetString(5),
            false);
    }

    private static void AddIdentityParameters(SqlCommand command, ProjectionIdentity identity)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
    }

    private string Table(string name) =>
        StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, name);
}
