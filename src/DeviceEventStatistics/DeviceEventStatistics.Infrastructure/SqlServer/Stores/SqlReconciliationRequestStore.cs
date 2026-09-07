using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlReconciliationRequestStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options) : IReconciliationRequestStore
{
    public async Task EnqueueAsync(
        IReadOnlyCollection<ReconciliationRequestSeed> requests,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return;
        }

        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await EnsureFencedAsync(session, lease, cancellationToken);
        foreach (var request in requests)
        {
            Validate(request);
            await ExecuteAsync(
                session,
                $"""
                DECLARE @requestId bigint;
                SELECT TOP (1) @requestId = [ReconciliationRequestId]
                FROM {Table("ReconciliationRequest")} WITH (UPDLOCK, HOLDLOCK)
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [CompanyId] = @companyId
                  AND [DeviceId] = @deviceId
                  AND [StateType] = @stateType
                  AND [Status] IN ('Pending', 'Processing', 'Completed')
                  AND [FromStatisticsDate] <= @toDate
                  AND [ToStatisticsDate] >= @fromDate
                ORDER BY [ReconciliationRequestId];

                IF @requestId IS NULL
                BEGIN
                    INSERT INTO {Table("ReconciliationRequest")}
                    (
                        [ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StateType],
                        [FromStatisticsDate], [ToStatisticsDate], [ReasonCode], [Status],
                        [RequestedAtUtc], [AttemptCount], [DirtyGeneration], [EvidenceEventId]
                    )
                    VALUES
                    (
                        @projectionName, @projectionVersion, @companyId, @deviceId, @stateType,
                        @fromDate, @toDate, @reasonCode, 'Pending', @requestedAtUtc, 0, 1, @evidenceEventId
                    );
                END
                ELSE
                BEGIN
                    UPDATE {Table("ReconciliationRequest")}
                    SET [FromStatisticsDate] = CASE WHEN [FromStatisticsDate] < @fromDate THEN [FromStatisticsDate] ELSE @fromDate END,
                        [ToStatisticsDate] = CASE WHEN [ToStatisticsDate] > @toDate THEN [ToStatisticsDate] ELSE @toDate END,
                        [ReasonCode] = @reasonCode,
                        [RequestedAtUtc] = CASE WHEN [RequestedAtUtc] < @requestedAtUtc THEN [RequestedAtUtc] ELSE @requestedAtUtc END,
                        [DirtyGeneration] = [DirtyGeneration] + 1,
                        [Status] = CASE WHEN [Status] = 'Processing' THEN [Status] ELSE 'Pending' END,
                        [NextAttemptAtUtc] = NULL,
                        [EvidenceEventId] = @evidenceEventId,
                        [ErrorSummary] = NULL
                    WHERE [ReconciliationRequestId] = @requestId;
                END;
                """,
                command =>
                {
                    AddLeaseIdentity(command, lease);
                    AddRequestParameters(command, request);
                },
                cancellationToken);
        }

        await session.CommitAsync(cancellationToken);
    }

    public async Task<ReconciliationClaim?> ClaimNextAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        TimeSpan claimDuration,
        int maximumAttempts,
        CancellationToken cancellationToken = default)
    {
        if (claimDuration <= TimeSpan.Zero || maximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(claimDuration));
        }

        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await EnsureFencedAsync(session, lease, cancellationToken);
        await ExecuteAsync(
            session,
            $"""
            UPDATE {Table("ReconciliationRequest")}
            SET [Status] = 'Pending', [ClaimOwner] = NULL, [ClaimEpoch] = NULL,
                [ClaimExpiresAtUtc] = NULL, [NextAttemptAtUtc] = NULL
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [Status] = 'Processing'
              AND [ClaimExpiresAtUtc] <= SYSUTCDATETIME();
            """,
            command => AddIdentityParameters(command, identity),
            cancellationToken);

        var requestId = await ReadScalarAsync<long>(
            session,
            $"""
            SELECT TOP (1) [ReconciliationRequestId]
            FROM {Table("ReconciliationRequest")} WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [ProjectionName] = @projectionName
              AND [ProjectionVersion] = @projectionVersion
              AND [Status] = 'Pending'
              AND ([NextAttemptAtUtc] IS NULL OR [NextAttemptAtUtc] <= SYSUTCDATETIME())
              AND [AttemptCount] < @maximumAttempts
            ORDER BY [RequestedAtUtc], [ReconciliationRequestId];
            """,
            command =>
            {
                AddIdentityParameters(command, identity);
                command.Parameters.Add(new SqlParameter("@maximumAttempts", maximumAttempts));
            },
            cancellationToken);
        if (requestId == 0)
        {
            await session.RollbackAsync(cancellationToken);
            return null;
        }

        await ExecuteAsync(
            session,
            $"""
            UPDATE {Table("ReconciliationRequest")}
            SET [Status] = 'Processing', [AttemptCount] = [AttemptCount] + 1,
                [ClaimOwner] = @owner, [ClaimEpoch] = @epoch,
                [ClaimExpiresAtUtc] = DATEADD(SECOND, @claimSeconds, SYSUTCDATETIME()),
                [StartedAtUtc] = COALESCE([StartedAtUtc], SYSUTCDATETIME()),
                [ErrorSummary] = NULL
            WHERE [ReconciliationRequestId] = @requestId
              AND [Status] = 'Pending';
            """,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@requestId", requestId));
                command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
                command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
                command.Parameters.Add(new SqlParameter("@claimSeconds", checked((int)Math.Ceiling(claimDuration.TotalSeconds))));
            },
            cancellationToken);
        var request = await ReadRequestAsync(session, requestId, cancellationToken) ??
            throw new InvalidOperationException(StatisticsContractConstants.Messages.MSG_RECONCILIATION_CLAIM_CONFLICT);
        await session.CommitAsync(cancellationToken);
        return new ReconciliationClaim(
            request,
            lease.Owner,
            lease.Epoch,
            request.ClaimExpiresAtUtc ?? throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_RECONCILIATION_CLAIM_CONFLICT));
    }

    public async Task<bool> RenewAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {Table("ReconciliationRequest")}
            SET [ClaimExpiresAtUtc] = DATEADD(SECOND, @claimSeconds, SYSUTCDATETIME())
            WHERE [ReconciliationRequestId] = @requestId
              AND [Status] = 'Processing'
              AND [ClaimOwner] = @owner
              AND [ClaimEpoch] = @epoch;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@requestId", claim.Request.RequestId));
        command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
        command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
        command.Parameters.Add(new SqlParameter("@claimSeconds", checked((int)Math.Ceiling(claimDuration.TotalSeconds))));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<ReconciliationClaim> LimitRangeAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        int maximumRangeDays,
        CancellationToken cancellationToken = default)
    {
        if (maximumRangeDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRangeDays));
        }

        var maximumTo = claim.Request.FromStatisticsDate.AddDays(maximumRangeDays - 1);
        if (maximumTo >= claim.Request.ToStatisticsDate)
        {
            return claim;
        }

        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await EnsureFencedAsync(session, lease, cancellationToken);
        await ExecuteAsync(
            session,
            $"""
            UPDATE {Table("ReconciliationRequest")}
            SET [ToStatisticsDate] = @chunkToDate
            WHERE [ReconciliationRequestId] = @requestId AND [Status] = 'Processing'
              AND [ClaimOwner] = @owner AND [ClaimEpoch] = @epoch
              AND [DirtyGeneration] = @dirtyGeneration;

            INSERT INTO {Table("ReconciliationRequest")}
            (
                [ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StateType],
                [FromStatisticsDate], [ToStatisticsDate], [ReasonCode], [Status], [RequestedAtUtc],
                [AttemptCount], [DirtyGeneration], [EvidenceEventId]
            )
            VALUES
            (
                @projectionName, @projectionVersion, @companyId, @deviceId, @stateType,
                @successorFromDate, @successorToDate, @reasonCode, 'Pending', @requestedAtUtc,
                0, @successorGeneration, @evidenceEventId
            );
            """,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@chunkToDate", maximumTo.ToDateTime(TimeOnly.MinValue)));
                command.Parameters.Add(new SqlParameter("@requestId", claim.Request.RequestId));
                command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
                command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
                command.Parameters.Add(new SqlParameter("@dirtyGeneration", claim.Request.DirtyGeneration));
                command.Parameters.Add(new SqlParameter("@projectionName", claim.Request.Identity.ProjectionName));
                command.Parameters.Add(new SqlParameter("@projectionVersion", claim.Request.Identity.ProjectionVersion));
                command.Parameters.Add(new SqlParameter("@companyId", claim.Request.Key.CompanyId));
                command.Parameters.Add(new SqlParameter("@deviceId", claim.Request.Key.DeviceId));
                command.Parameters.Add(new SqlParameter("@stateType", claim.Request.Key.StateType));
                command.Parameters.Add(new SqlParameter("@successorFromDate", maximumTo.AddDays(1).ToDateTime(TimeOnly.MinValue)));
                command.Parameters.Add(new SqlParameter("@successorToDate", claim.Request.ToStatisticsDate.ToDateTime(TimeOnly.MinValue)));
                command.Parameters.Add(new SqlParameter("@reasonCode", ReconciliationReasonCodes.ForwardPropagation));
                command.Parameters.Add(new SqlParameter("@requestedAtUtc", claim.Request.RequestedAtUtc.UtcDateTime));
                command.Parameters.Add(new SqlParameter("@successorGeneration", claim.Request.DirtyGeneration + 1));
                command.Parameters.Add(new SqlParameter(
                    "@evidenceEventId",
                    claim.Request.EvidenceEventId is null
                        ? DBNull.Value
                        : Convert.FromHexString(claim.Request.EvidenceEventId)));
            },
            cancellationToken);
        await session.CommitAsync(cancellationToken);
        return claim with
        {
            Request = claim.Request with { ToStatisticsDate = maximumTo }
        };
    }

    public async Task<ReconciliationClaim> ExtendToCurrentEdgeAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        DateOnly currentEdgeDate,
        CancellationToken cancellationToken = default)
    {
        if (currentEdgeDate <= claim.Request.ToStatisticsDate)
        {
            return claim;
        }

        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await EnsureFencedAsync(session, lease, cancellationToken);
        await ExecuteAsync(
            session,
            $"""
            UPDATE {Table("ReconciliationRequest")}
            SET [ToStatisticsDate] = @toDate, [DirtyGeneration] = [DirtyGeneration] + 1
            WHERE [ReconciliationRequestId] = @requestId AND [Status] = 'Processing'
              AND [ClaimOwner] = @owner AND [ClaimEpoch] = @epoch
              AND [DirtyGeneration] = @dirtyGeneration;
            """,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@toDate", currentEdgeDate.ToDateTime(TimeOnly.MinValue)));
                command.Parameters.Add(new SqlParameter("@requestId", claim.Request.RequestId));
                command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
                command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
                command.Parameters.Add(new SqlParameter("@dirtyGeneration", claim.Request.DirtyGeneration));
            },
            cancellationToken);
        await session.CommitAsync(cancellationToken);
        return claim with
        {
            Request = claim.Request with
            {
                ToStatisticsDate = currentEdgeDate,
                DirtyGeneration = claim.Request.DirtyGeneration + 1
            }
        };
    }

    public async Task FailAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        string errorSummary,
        bool permanent,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorSummary);
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {Table("ReconciliationRequest")}
            SET [Status] = @status,
                [NextAttemptAtUtc] = CASE WHEN @status = 'Pending' THEN DATEADD(SECOND, @retrySeconds, SYSUTCDATETIME()) ELSE NULL END,
                [ClaimOwner] = NULL, [ClaimEpoch] = NULL, [ClaimExpiresAtUtc] = NULL,
                [CompletedAtUtc] = CASE WHEN @status = 'Failed' THEN SYSUTCDATETIME() ELSE [CompletedAtUtc] END,
                [ErrorSummary] = @errorSummary
            WHERE [ReconciliationRequestId] = @requestId
              AND [Status] = 'Processing'
              AND [ClaimOwner] = @owner
              AND [ClaimEpoch] = @epoch;
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@status", permanent ? ReconciliationRequestStatuses.Failed : ReconciliationRequestStatuses.Pending));
        command.Parameters.Add(new SqlParameter("@retrySeconds", checked((int)Math.Ceiling(retryDelay.TotalSeconds))));
        command.Parameters.Add(new SqlParameter("@errorSummary", errorSummary[..Math.Min(errorSummary.Length, 1000)]));
        command.Parameters.Add(new SqlParameter("@requestId", claim.Request.RequestId));
        command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
        command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(StatisticsContractConstants.Messages.MSG_RECONCILIATION_CLAIM_CONFLICT);
        }
    }

    private async Task<ReconciliationRequest?> ReadRequestAsync(
        SqlProjectionSession session,
        long requestId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT [ReconciliationRequestId], [ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StateType],
                   [FromStatisticsDate], [ToStatisticsDate], [ReasonCode], [Status], [AttemptCount],
                   [NextAttemptAtUtc], [ClaimOwner], [ClaimEpoch], [ClaimExpiresAtUtc], [DirtyGeneration],
                   [RequestedAtUtc], [ErrorSummary], [EvidenceEventId]
            FROM {Table("ReconciliationRequest")}
            WHERE [ReconciliationRequestId] = @requestId;
            """;
        command.Parameters.Add(new SqlParameter("@requestId", requestId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return !await reader.ReadAsync(cancellationToken) ? null : MapRequest(reader);
    }

    private async Task<T> ReadScalarAsync<T>(
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
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, typeof(T));
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

    private async Task EnsureFencedAsync(
        SqlProjectionSession session,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT 1 FROM {Table("ProjectionCheckpoint")} WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProjectionName] = @projectionName AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey AND [LeaseOwner] = @owner
              AND [LeaseEpoch] = @epoch AND [LeaseExpiresAtUtc] > SYSUTCDATETIME();
            """;
        AddLeaseIdentity(command, lease);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new InvalidOperationException(StatisticsContractConstants.LeaseErrors.NotOwned);
        }
    }

    private static void Validate(ReconciliationRequestSeed request)
    {
        if (request.FromStatisticsDate > request.ToStatisticsDate ||
            request.Key.CompanyId <= 0 || request.Key.DeviceId <= 0 ||
            string.IsNullOrWhiteSpace(request.Key.StateType) ||
            !IsEventId(request.EvidenceEventId))
        {
            throw new ArgumentException(StatisticsContractConstants.Messages.MSG_RECONCILIATION_REQUEST_INVALID);
        }
    }

    private static bool IsEventId(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ReconciliationRequest MapRequest(SqlDataReader reader) =>
        new(
            reader.GetInt32(0),
            new ProjectionIdentity(reader.GetString(1), reader.GetInt32(2), StatisticsContractConstants.DefaultPartitionKey),
            new(reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5)),
            DateOnly.FromDateTime(reader.GetDateTime(6)),
            DateOnly.FromDateTime(reader.GetDateTime(7)),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetInt32(10),
            ReadDate(reader, 11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt64(13),
            ReadDate(reader, 14),
            reader.GetInt64(15),
            ReadDate(reader, 16) ?? DateTimeOffset.UnixEpoch,
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : Convert.ToHexString((byte[])reader[18]).ToLowerInvariant());

    private static DateTimeOffset? ReadDate(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static void AddRequestParameters(SqlCommand command, ReconciliationRequestSeed request)
    {
        command.Parameters.Add(new SqlParameter("@companyId", request.Key.CompanyId));
        command.Parameters.Add(new SqlParameter("@deviceId", request.Key.DeviceId));
        command.Parameters.Add(new SqlParameter("@stateType", request.Key.StateType));
        command.Parameters.Add(new SqlParameter("@fromDate", request.FromStatisticsDate.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new SqlParameter("@toDate", request.ToStatisticsDate.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new SqlParameter("@reasonCode", request.ReasonCode));
        command.Parameters.Add(new SqlParameter("@requestedAtUtc", request.RequestedAtUtc.UtcDateTime));
        command.Parameters.Add(new SqlParameter("@evidenceEventId", Convert.FromHexString(request.EvidenceEventId)));
    }

    private static void AddIdentityParameters(SqlCommand command, ProjectionIdentity identity)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
    }

    private static void AddLeaseIdentity(SqlCommand command, ProjectionLeaseToken lease)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", lease.Identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", lease.Identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@partitionKey", lease.Identity.PartitionKey));
        command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
        command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
    }

    private string Table(string name) =>
        StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, name);
}
