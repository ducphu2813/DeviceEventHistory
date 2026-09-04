using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionCheckpointStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options) : IProjectionCheckpointStore
{
    public async Task<ProjectionCheckpoint> GetOrCreateAsync(
        ProjectionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var checkpoint = await ReadAsync(connection, transaction, identity, cancellationToken);
        if (checkpoint is null)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO {Table("ProjectionCheckpoint")}
                    ([ProjectionName], [ProjectionVersion], [PartitionKey], [LastBatchSize],
                     [LeaseEpoch], [DataRevision], [UpdatedAtUtc])
                VALUES (@projectionName, @projectionVersion, @partitionKey, 0, 0, 0, SYSUTCDATETIME());
                """;
            insert.CommandTimeout = options.CommandTimeoutSeconds;
            AddIdentityParameters(insert, identity);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            checkpoint = await ReadAsync(connection, transaction, identity, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return checkpoint ?? throw new InvalidOperationException(
            StatisticsContractConstants.Messages.MSG_SQL_CHECKPOINT_CREATE_FAILED);
    }

    public async Task<bool> AdvanceAsync(
        ProjectionCheckpoint expected,
        ProjectionCheckpoint next,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var updated = await AdvanceAsync(connection, transaction, expected, next, lease, cancellationToken);
        if (!updated)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> AdvanceAsync(
        SqlProjectionSession session,
        ProjectionCheckpoint expected,
        ProjectionCheckpoint next,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default) =>
        AdvanceAsync(session.Connection, session.Transaction, expected, next, lease, cancellationToken);

    public async Task<bool> IsEquivalentAsync(
        SqlProjectionSession session,
        ProjectionCheckpoint checkpoint,
        ProjectionLeaseToken lease,
        bool allowOneRevisionAhead = false,
        CancellationToken cancellationToken = default)
    {
        if (checkpoint.Identity != lease.Identity)
        {
            return false;
        }

        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = $"""
            SELECT [LastPersistedAtUtc], [LastEventId], [LastProcessedAtUtc], [LastBatchSize],
                   [SweepFromAtUtc], [SweepToAtUtc], [SweepLastPersistedAtUtc], [SweepLastEventId],
                   [DataRevision]
            FROM {Table("ProjectionCheckpoint")}
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey
              AND [LeaseOwner] = @owner
              AND [LeaseEpoch] = @epoch
              AND [LeaseExpiresAtUtc] > SYSUTCDATETIME();
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddIdentityParameters(command, checkpoint.Identity);
        command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
        command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return false;

        return DateEquals(reader, 0, checkpoint.LastPersistedAtUtc) &&
               StringEquals(reader, 1, checkpoint.LastEventId) &&
               DateEquals(reader, 2, checkpoint.LastProcessedAtUtc) &&
               reader.GetInt32(3) == checkpoint.LastBatchSize &&
               DateEquals(reader, 4, checkpoint.SweepFromAtUtc) &&
               DateEquals(reader, 5, checkpoint.SweepToAtUtc) &&
               DateEquals(reader, 6, checkpoint.SweepLastPersistedAtUtc) &&
               StringEquals(reader, 7, checkpoint.SweepLastEventId) &&
               (reader.GetInt64(8) == checkpoint.DataRevision ||
                allowOneRevisionAhead && reader.GetInt64(8) == checkpoint.DataRevision + 1);
    }

    private async Task<bool> AdvanceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ProjectionCheckpoint expected,
        ProjectionCheckpoint next,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        if (expected.Identity != next.Identity || next.Identity != lease.Identity)
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_SQL_CHECKPOINT_IDENTITY_MISMATCH);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {Table("ProjectionCheckpoint")}
            SET [LastPersistedAtUtc] = @lastPersistedAtUtc,
                [LastEventId] = @lastEventId,
                [LastProcessedAtUtc] = @lastProcessedAtUtc,
                [LastBatchSize] = @lastBatchSize,
                [SweepFromAtUtc] = @sweepFromAtUtc,
                [SweepToAtUtc] = @sweepToAtUtc,
                [SweepLastPersistedAtUtc] = @sweepLastPersistedAtUtc,
                [SweepLastEventId] = @sweepLastEventId,
                [DataRevision] = @dataRevision,
                [UpdatedAtUtc] = SYSUTCDATETIME()
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey
              AND [LeaseOwner] = @owner
              AND [LeaseEpoch] = @epoch
              AND [LeaseExpiresAtUtc] > SYSUTCDATETIME()
              AND (@rowVersion IS NULL OR [Version] = @rowVersion);
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddIdentityParameters(command, expected.Identity);
        command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
        command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
        AddDateParameter(command, "@lastPersistedAtUtc", next.LastPersistedAtUtc);
        AddStringParameter(command, "@lastEventId", next.LastEventId);
        AddDateParameter(command, "@lastProcessedAtUtc", next.LastProcessedAtUtc);
        command.Parameters.Add(new SqlParameter("@lastBatchSize", next.LastBatchSize));
        AddDateParameter(command, "@sweepFromAtUtc", next.SweepFromAtUtc);
        AddDateParameter(command, "@sweepToAtUtc", next.SweepToAtUtc);
        AddDateParameter(command, "@sweepLastPersistedAtUtc", next.SweepLastPersistedAtUtc);
        AddStringParameter(command, "@sweepLastEventId", next.SweepLastEventId);
        command.Parameters.Add(new SqlParameter("@dataRevision", next.DataRevision));
        command.Parameters.Add(new SqlParameter("@rowVersion", (object?)expected.RowVersion ?? DBNull.Value));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<ProjectionCheckpoint?> ReadAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ProjectionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT [LastPersistedAtUtc], [LastEventId], [LastProcessedAtUtc], [LastBatchSize],
                   [SweepFromAtUtc], [SweepToAtUtc], [SweepLastPersistedAtUtc], [SweepLastEventId],
                   [DataRevision], [Version]
            FROM {Table("ProjectionCheckpoint")} WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddIdentityParameters(command, identity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return !await reader.ReadAsync(cancellationToken) ? null : new ProjectionCheckpoint(
            identity,
            ReadDate(reader, 0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            ReadDate(reader, 2),
            reader.GetInt32(3),
            ReadDate(reader, 4),
            ReadDate(reader, 5),
            ReadDate(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt64(8),
            reader.IsDBNull(9) ? null : ((byte[])reader[9]).ToArray());
    }

    private static DateTimeOffset? ReadDate(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static void AddIdentityParameters(SqlCommand command, ProjectionIdentity identity)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@partitionKey", identity.PartitionKey));
    }

    private static void AddDateParameter(SqlCommand command, string name, DateTimeOffset? value) =>
        command.Parameters.Add(new SqlParameter(name, value?.UtcDateTime ?? (object)DBNull.Value));

    private static void AddStringParameter(SqlCommand command, string name, string? value) =>
        command.Parameters.Add(new SqlParameter(name, (object?)value ?? DBNull.Value));

    private static bool DateEquals(SqlDataReader reader, int ordinal, DateTimeOffset? expected) =>
        (reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal)) == expected;

    private static bool StringEquals(SqlDataReader reader, int ordinal, string? expected) =>
        (reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal)) == expected;

    private string Table(string tableName) =>
        $"[{options.SchemaName}].[{tableName}]";
}
