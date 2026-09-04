using System.Data;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionLeaseStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options) : IProjectionLeaseStore
{
    public async Task<LeaseAcquireResult> AcquireAsync(
        ProjectionIdentity identity,
        string owner,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireApplicationLockAsync(connection, transaction, identity, cancellationToken);

        var current = await ReadLeaseAsync(connection, transaction, identity, cancellationToken);
        if (current is null)
        {
            var insertSql = $"""
                INSERT INTO {Table("ProjectionCheckpoint")}
                    ([ProjectionName], [ProjectionVersion], [PartitionKey], [LastBatchSize],
                     [LeaseOwner], [LeaseExpiresAtUtc], [LeaseEpoch], [DataRevision], [UpdatedAtUtc])
                OUTPUT INSERTED.[LeaseEpoch], INSERTED.[LeaseExpiresAtUtc]
                VALUES (@projectionName, @projectionVersion, @partitionKey, 0,
                        @owner, DATEADD(SECOND, @durationSeconds, SYSUTCDATETIME()), 1, 0, SYSUTCDATETIME());
                """;
            var inserted = await ExecuteLeaseMutationAsync(
                connection, transaction, insertSql, identity, owner, duration, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LeaseAcquireResult(true, new ProjectionLeaseToken(identity, owner, inserted.Epoch, inserted.ExpiresAtUtc));
        }

        if (current.IsActive && !string.Equals(current.Owner, owner, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new LeaseAcquireResult(false, null, current.ExpiresAtUtc);
        }

        var mutationSql = current.IsActive
            ? $"""
                UPDATE {Table("ProjectionCheckpoint")}
                SET [LeaseExpiresAtUtc] = DATEADD(SECOND, @durationSeconds, SYSUTCDATETIME()),
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                OUTPUT INSERTED.[LeaseEpoch], INSERTED.[LeaseExpiresAtUtc]
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [PartitionKey] = @partitionKey
                  AND [LeaseOwner] = @owner;
                """
            : $"""
                UPDATE {Table("ProjectionCheckpoint")}
                SET [LeaseOwner] = @owner,
                    [LeaseExpiresAtUtc] = DATEADD(SECOND, @durationSeconds, SYSUTCDATETIME()),
                    [LeaseEpoch] = [LeaseEpoch] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                OUTPUT INSERTED.[LeaseEpoch], INSERTED.[LeaseExpiresAtUtc]
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [PartitionKey] = @partitionKey;
                """;
        var updated = await ExecuteLeaseMutationAsync(
            connection, transaction, mutationSql, identity, owner, duration, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LeaseAcquireResult(true, new ProjectionLeaseToken(identity, owner, updated.Epoch, updated.ExpiresAtUtc));
    }

    public async Task<ProjectionLeaseToken?> RenewAsync(
        ProjectionLeaseToken lease,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var sql = $"""
            UPDATE {Table("ProjectionCheckpoint")}
            SET [LeaseExpiresAtUtc] = DATEADD(SECOND, @durationSeconds, SYSUTCDATETIME()),
                [UpdatedAtUtc] = SYSUTCDATETIME()
            OUTPUT INSERTED.[LeaseExpiresAtUtc]
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey
              AND [LeaseOwner] = @owner
              AND [LeaseEpoch] = @epoch
              AND [LeaseExpiresAtUtc] > SYSUTCDATETIME();
            """;
        await using var command = CreateCommand(connection, null, sql, lease.Identity, lease.Owner, duration, lease.Epoch);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : lease with { ExpiresAtUtc = ToUtcDateTimeOffset((DateTime)value) };
    }

    public async Task<bool> ReleaseAsync(
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var sql = $"""
            UPDATE {Table("ProjectionCheckpoint")}
            SET [LeaseOwner] = NULL,
                [LeaseExpiresAtUtc] = NULL,
                [UpdatedAtUtc] = SYSUTCDATETIME()
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey
              AND [LeaseOwner] = @owner
              AND [LeaseEpoch] = @epoch;
            """;
        await using var command = CreateCommand(connection, null, sql, lease.Identity, lease.Owner, null, lease.Epoch);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task AcquireApplicationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ProjectionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 0;
            SELECT @result;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@resource", $"{identity.ProjectionName}:{identity.ProjectionVersion}:{identity.PartitionKey}"));
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_SQL_LEASE_APPLOCK_UNAVAILABLE);
        }
    }

    private async Task<LeaseState?> ReadLeaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ProjectionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT [LeaseOwner], [LeaseExpiresAtUtc], [LeaseEpoch],
                   CASE WHEN [LeaseExpiresAtUtc] > SYSUTCDATETIME() THEN 1 ELSE 0 END
            FROM {Table("ProjectionCheckpoint")} WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddIdentityParameters(command, identity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new LeaseState(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : ToUtcDateTimeOffset(reader.GetDateTime(1)),
            reader.GetInt64(2),
            reader.GetInt32(3) == 1);
    }

    private async Task<(long Epoch, DateTimeOffset ExpiresAtUtc)> ExecuteLeaseMutationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        ProjectionIdentity identity,
        string owner,
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, sql, identity, owner, duration, null);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_SQL_LEASE_MUTATION_FAILED);
        }

        return (reader.GetInt64(0), ToUtcDateTimeOffset(reader.GetDateTime(1)));
    }

    private SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        ProjectionIdentity identity,
        string owner,
        TimeSpan? duration,
        long? epoch)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddIdentityParameters(command, identity);
        command.Parameters.Add(new SqlParameter("@owner", owner));
        if (duration is not null)
        {
            var seconds = checked((int)Math.Ceiling(duration.Value.TotalSeconds));
            command.Parameters.Add(new SqlParameter("@durationSeconds", seconds));
        }
        if (epoch is not null) command.Parameters.Add(new SqlParameter("@epoch", epoch.Value));
        return command;
    }

    private static void AddIdentityParameters(SqlCommand command, ProjectionIdentity identity)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@partitionKey", identity.PartitionKey));
    }

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private string Table(string tableName) =>
        $"[{options.SchemaName}].[{tableName}]";

    private sealed record LeaseState(string? Owner, DateTimeOffset? ExpiresAtUtc, long Epoch, bool IsActive);
}
