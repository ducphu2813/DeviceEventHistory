using System.Data;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.State;
using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Infrastructure.SqlServer.Mapping;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionRebuildStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options,
    SqlProjectionBatchOperations operations,
    ProjectionTvpMapper mapper,
    TimeProvider timeProvider) : IProjectionRebuildStore
{
    public async Task<ReconciliationSnapshot> CaptureAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await operations.EnsureFencedAsync(session, claim.Request.Identity, lease, cancellationToken);
        var from = BucketStart(claim.Request.FromStatisticsDate);
        var to = BucketStart(claim.Request.ToStatisticsDate.AddDays(1));
        var revision = await ReadRevisionAsync(session, lease, cancellationToken);
        var membership = await ReadMembershipAsync(session, claim.Request.Identity, from, to, cancellationToken);
        var opening = await ReadOpeningCursorAsync(session, claim.Request, cancellationToken);
        await session.RollbackAsync(cancellationToken);
        return new ReconciliationSnapshot(
            Guid.NewGuid(),
            claim,
            from,
            to,
            revision,
            membership,
            opening);
    }

    public async Task StageAsync(
        ReconciliationSnapshot snapshot,
        ReconciliationSourceResult result,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await DeleteStagingAsync(session, snapshot.RunId, cancellationToken);
        await BulkCopyAsync(session, "ProjectionStagingEvent", CreateEventTable(snapshot, result), cancellationToken);
        await BulkCopyAsync(session, "ProjectionStagingDaily", CreateMetricTable(snapshot, result), cancellationToken);
        await BulkCopyAsync(session, "ProjectionStagingSummary", CreateSummaryTable(snapshot, result), cancellationToken);
        await BulkCopyAsync(session, "ProjectionStagingState", CreateStateTable(snapshot, result), cancellationToken);
        await BulkCopyAsync(session, "ProjectionStagingCoverage", CreateCoverageTable(snapshot, result), cancellationToken);
        await BulkCopyAsync(session, "ProjectionStagingQuality", CreateQualityTable(snapshot, result), cancellationToken);
        await BulkCopyAsync(session, "ProjectionStagingCursor", CreateCursorTable(snapshot, result), cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    public async Task<ReconciliationPublishResult> PublishAsync(
        ReconciliationSnapshot snapshot,
        ReconciliationSourceResult result,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await operations.EnsureFencedAsync(session, snapshot.Claim.Request.Identity, lease, cancellationToken);
        await VerifyPublishTokenAsync(session, snapshot, lease, cancellationToken);

        var request = snapshot.Claim.Request;
        var affectedRows = result.MetricContributions.Count +
                           result.DeviceSummaries.Count +
                           result.StateDailyContributions.Count +
                           result.Coverage.Count;
        await ExecuteAsync(
            session,
            $"""
            INSERT INTO {Table("ProcessedEvent")}
            (
                [ProjectionName], [ProjectionVersion], [EventId], [SourceDocumentId], [SourceKind],
                [SourcePersistedAtUtc], [TimelineAtUtc], [StatisticsDate], [MappingVersion],
                [Outcome], [ProcessedAtUtc]
            )
            SELECT @projectionName, @projectionVersion, input.[EventId], input.[SourceDocumentId],
                   input.[SourceKind], input.[SourcePersistedAtUtc], input.[TimelineAtUtc],
                   input.[StatisticsDate], input.[MappingVersion], input.[Outcome], SYSUTCDATETIME()
            FROM @processed input
            WHERE NOT EXISTS
            (
                SELECT 1 FROM {Table("ProcessedEvent")} existing WITH (UPDLOCK, HOLDLOCK)
                WHERE existing.[ProjectionName] = @projectionName
                  AND existing.[ProjectionVersion] = @projectionVersion
                  AND existing.[EventId] = input.[EventId]
            );

            DELETE FROM {Table("DeviceEventDaily")}
            WHERE [ProjectionVersion] = @projectionVersion
              AND [CompanyId] = @companyId AND [DeviceId] = @deviceId
              AND [StatisticsDate] BETWEEN @fromDate AND @toDate;
            DELETE FROM {Table("DeviceDailySnapshot")}
            WHERE [ProjectionVersion] = @projectionVersion
              AND [CompanyId] = @companyId AND [DeviceId] = @deviceId
              AND [StatisticsDate] BETWEEN @fromDate AND @toDate;
            DELETE FROM {Table("DeviceStateDaily")}
            WHERE [ProjectionVersion] = @projectionVersion
              AND [CompanyId] = @companyId AND [DeviceId] = @deviceId
              AND [StatisticsDate] BETWEEN @fromDate AND @toDate;
            DELETE FROM {Table("IngestionQualityDaily")}
            WHERE [ProjectionVersion] = @projectionVersion
              AND [CompanyId] = @companyId
              AND [StatisticsDate] BETWEEN @fromDate AND @toDate;

            INSERT INTO {Table("DeviceEventDaily")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind],
                [EventCount], [ParsedWithWarningsCount], [OccurredTimeBasisCount], [ReceivedTimeBasisCount],
                [FirstEventAtUtc], [LastEventAtUtc], [LastSourcePersistedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]
            )
            SELECT [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind],
                   [EventCount], [ParsedWithWarningsCount], [OccurredTimeBasisCount], [ReceivedTimeBasisCount],
                   [FirstEventAtUtc], [LastEventAtUtc], COALESCE([LastSourcePersistedAtUtc], SYSUTCDATETIME()),
                   SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM {Table("ProjectionStagingDaily")}
            WHERE [RunId] = @runId;

            INSERT INTO {Table("DeviceDailySnapshot")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [TimeZoneId],
                [BucketStartAtUtc], [BucketEndAtUtc], [OpeningConnectionStatus], [ClosingConnectionStatus],
                [ConnectedEventCount], [DisconnectedEventCount], [ReconnectCount], [TotalEventCount],
                [ErrorEventCount], [WarningEventCount], [FirstEventAtUtc], [LastEventAtUtc], [IsFinalized],
                [CalculatedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]
            )
            SELECT [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], @timeZoneId,
                   DATEADD(HOUR, -7, CONVERT(datetime2(7), [StatisticsDate])),
                   DATEADD(DAY, 1, DATEADD(HOUR, -7, CONVERT(datetime2(7), [StatisticsDate]))),
                   'unknown', 'unknown', 0, 0, 0, [EventCount], [ErrorEventCount], [WarningEventCount],
                   [FirstEventAtUtc], [LastEventAtUtc], 0, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM {Table("ProjectionStagingSummary")}
            WHERE [RunId] = @runId;

            UPDATE target
            SET [OpeningConnectionStatus] = state.[OpeningState],
                [ClosingConnectionStatus] = state.[ClosingState],
                [ConnectedEventCount] = state.[ConnectedEventCount],
                [DisconnectedEventCount] = state.[DisconnectedEventCount],
                [ReconnectCount] = state.[ReconnectCount],
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("DeviceDailySnapshot")} target
            INNER JOIN {Table("ProjectionStagingState")} state
                ON state.[RunId] = @runId
               AND state.[ProjectionVersion] = target.[ProjectionVersion]
               AND state.[CompanyId] = target.[CompanyId]
               AND state.[DeviceId] = target.[DeviceId]
               AND state.[StatisticsDate] = target.[StatisticsDate]
               AND state.[StateType] = 'device_connection'
            WHERE target.[ProjectionVersion] = @projectionVersion
              AND target.[CompanyId] = @companyId AND target.[DeviceId] = @deviceId
              AND target.[StatisticsDate] BETWEEN @fromDate AND @toDate;

            INSERT INTO {Table("DeviceStateDaily")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [StateType],
                [BucketStartAtUtc], [BucketEndAtUtc], [CalculatedThroughAtUtc], [OpeningConnectionStatus],
                [ClosingConnectionStatus], [OnlineSeconds], [OfflineSeconds], [UnknownSeconds],
                [ConnectedEventCount], [DisconnectedEventCount], [ReconnectCount], [OpeningEvidenceKind],
                [OpeningEvidenceEventId], [IsDirty], [IsFinalized], [CoverageStatus], [CalculatedAtUtc],
                [CreatedAtUtc], [UpdatedAtUtc]
            )
            SELECT [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [StateType],
                   [BucketStartAtUtc], [BucketEndAtUtc], [CalculatedThroughAtUtc], [OpeningState], [ClosingState],
                   [OnlineSeconds], [OfflineSeconds], [UnknownSeconds], [ConnectedEventCount],
                   [DisconnectedEventCount], [ReconnectCount], COALESCE([OpeningEvidenceKind], 'no_predecessor'),
                   [OpeningEvidenceEventId], [IsDirty], [IsFinalized], COALESCE([CoverageStatus], 'partial'),
                   SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM {Table("ProjectionStagingState")}
            WHERE [RunId] = @runId;

            UPDATE target
            SET [CurrentState] = staged.[CurrentState], [StateSinceAtUtc] = staged.[StateSinceAtUtc],
                [AccountedThroughAtUtc] = staged.[AccountedThroughAtUtc], [LastTimelineAtUtc] = staged.[LastTimelineAtUtc],
                [LastEventId] = staged.[LastEventId], [OpeningEvidenceKind] = staged.[OpeningEvidenceKind],
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("DeviceStateCursor")} target
            INNER JOIN {Table("ProjectionStagingCursor")} staged
                ON staged.[RunId] = @runId
               AND staged.[ProjectionVersion] = target.[ProjectionVersion]
               AND staged.[CompanyId] = target.[CompanyId]
               AND staged.[DeviceId] = target.[DeviceId]
               AND staged.[StateType] = target.[StateType];

            INSERT INTO {Table("DeviceStateCursor")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StateType], [CurrentState], [StateSinceAtUtc],
                [AccountedThroughAtUtc], [LastTimelineAtUtc], [LastEventId], [OpeningEvidenceKind], [UpdatedAtUtc]
            )
            SELECT [ProjectionVersion], [CompanyId], [DeviceId], [StateType], [CurrentState], [StateSinceAtUtc],
                   [AccountedThroughAtUtc], [LastTimelineAtUtc], [LastEventId], [OpeningEvidenceKind], SYSUTCDATETIME()
            FROM {Table("ProjectionStagingCursor")} staged
            WHERE [RunId] = @runId
              AND NOT EXISTS
              (
                  SELECT 1 FROM {Table("DeviceStateCursor")} existing
                  WHERE existing.[ProjectionVersion] = staged.[ProjectionVersion]
                    AND existing.[CompanyId] = staged.[CompanyId]
                    AND existing.[DeviceId] = staged.[DeviceId]
                    AND existing.[StateType] = staged.[StateType]
              );

            INSERT INTO {Table("IngestionQualityDaily")}
            (
                [ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode],
                [EventCount], [FirstSeenAtUtc], [LastSeenAtUtc], [UpdatedAtUtc]
            )
            SELECT [ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode],
                   [EventCount], [FirstSeenAtUtc], [LastSeenAtUtc], SYSUTCDATETIME()
            FROM {Table("ProjectionStagingQuality")}
            WHERE [RunId] = @runId;

            UPDATE coverage
            SET [CoverageStatus] = staged.[CoverageStatus], [CoveredFromAtUtc] = staged.[CoveredFromAtUtc],
                [CoveredThroughAtUtc] = staged.[CoveredThroughAtUtc], [ReasonCode] = staged.[ReasonCode],
                [RunId] = @runId, [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("ProjectionCoverage")} coverage
            INNER JOIN {Table("ProjectionStagingCoverage")} staged
                ON staged.[RunId] = @runId
               AND staged.[ProjectionName] = coverage.[ProjectionName]
               AND staged.[ProjectionVersion] = coverage.[ProjectionVersion]
               AND staged.[CompanyId] = coverage.[CompanyId]
               AND staged.[DeviceId] = coverage.[DeviceId]
               AND staged.[StatisticsDate] = coverage.[StatisticsDate]
               AND staged.[CoverageKind] = coverage.[CoverageKind];

            INSERT INTO {Table("ProjectionCoverage")}
            (
                [ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [CoverageKind],
                [CoverageStatus], [CoveredFromAtUtc], [CoveredThroughAtUtc], [ReasonCode], [RunId], [UpdatedAtUtc]
            )
            SELECT staged.[ProjectionName], staged.[ProjectionVersion], staged.[CompanyId], staged.[DeviceId],
                   staged.[StatisticsDate], staged.[CoverageKind], staged.[CoverageStatus], staged.[CoveredFromAtUtc],
                   staged.[CoveredThroughAtUtc], staged.[ReasonCode], @runId, SYSUTCDATETIME()
            FROM {Table("ProjectionStagingCoverage")} staged
            WHERE staged.[RunId] = @runId
              AND NOT EXISTS
              (
                  SELECT 1 FROM {Table("ProjectionCoverage")} existing
                  WHERE existing.[ProjectionName] = staged.[ProjectionName]
                    AND existing.[ProjectionVersion] = staged.[ProjectionVersion]
                    AND existing.[CompanyId] = staged.[CompanyId]
                    AND existing.[DeviceId] = staged.[DeviceId]
                    AND existing.[StatisticsDate] = staged.[StatisticsDate]
                    AND existing.[CoverageKind] = staged.[CoverageKind]
              );

            UPDATE {Table("ReconciliationRequest")}
            SET [Status] = 'Completed', [CompletedAtUtc] = SYSUTCDATETIME(),
                [ClaimOwner] = NULL, [ClaimEpoch] = NULL, [ClaimExpiresAtUtc] = NULL,
                [ErrorSummary] = NULL
            WHERE [ReconciliationRequestId] = @requestId AND [Status] = 'Processing'
              AND [ClaimOwner] = @owner AND [ClaimEpoch] = @epoch
              AND [DirtyGeneration] = @dirtyGeneration;

            UPDATE {Table("ProjectionCheckpoint")}
            SET [DataRevision] = @nextRevision, [UpdatedAtUtc] = SYSUTCDATETIME()
            WHERE [ProjectionName] = @projectionName AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey AND [LeaseOwner] = @owner AND [LeaseEpoch] = @epoch
              AND [LeaseExpiresAtUtc] > SYSUTCDATETIME() AND [DataRevision] = @capturedRevision;

            UPDATE {Table("ProjectionRun")}
            SET [CompletedAtUtc] = SYSUTCDATETIME(), [Status] = 'succeeded',
                [ReadEventCount] = @readEventCount, [AggregatedEventCount] = @aggregatedEventCount,
                [DuplicateEventCount] = 0, [IgnoredEventCount] = @ignoredEventCount,
                [FailureEventCount] = @failureEventCount, [AffectedRowCount] = @affectedRowCount,
                [CapturedDataRevision] = @capturedRevision, [ErrorSummary] = NULL
            WHERE [RunId] = @runId;

            IF @@ROWCOUNT = 0
            INSERT INTO {Table("ProjectionRun")}
            (
                [RunId], [ProjectionName], [ProjectionVersion], [RunType], [RequestedFromDate],
                [RequestedToDate], [RequestedCompanyId], [StartedAtUtc], [CompletedAtUtc], [Status],
                [ReadEventCount], [AggregatedEventCount], [DuplicateEventCount], [IgnoredEventCount],
                [FailureEventCount], [AffectedRowCount], [CapturedDataRevision]
            )
            VALUES
            (
                @runId, @projectionName, @projectionVersion, @runType, @fromDate, @toDate, @companyId,
                @startedAtUtc, SYSUTCDATETIME(), 'succeeded', @readEventCount, @aggregatedEventCount, 0, @ignoredEventCount,
                @failureEventCount, @affectedRowCount, @capturedRevision
            );
            """,
            command =>
            {
                AddPublishParameters(command, snapshot, lease, result);
                var processed = command.Parameters.Add("@processed", System.Data.SqlDbType.Structured);
                processed.TypeName = $"{options.SchemaName}.ProjectionProcessedEventType";
                processed.Value = mapper.MapProcessedEvents(result.ProcessedEvents);
                command.Parameters.Add(new SqlParameter("@timeZoneId", StatisticsContractConstants.DefaultTimeZoneId));
            },
            cancellationToken);
        await VerifyPublishedAsync(session, snapshot, lease, cancellationToken);

        await DeleteStagingAsync(session, snapshot.RunId, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return new ReconciliationPublishResult(snapshot.RunId, affectedRows, snapshot.CapturedDataRevision + 1);
    }

    public async Task CleanupAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await DeleteStagingAsync(session, runId, cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private async Task VerifyPublishTokenAsync(
        SqlProjectionSession session,
        ReconciliationSnapshot snapshot,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            IF NOT EXISTS
            (
                SELECT 1 FROM {Table("ReconciliationRequest")}
                WHERE [ReconciliationRequestId] = @requestId AND [Status] = 'Processing'
                  AND [ClaimOwner] = @owner AND [ClaimEpoch] = @epoch
                  AND [DirtyGeneration] = @dirtyGeneration
            ) OR NOT EXISTS
            (
                SELECT 1 FROM {Table("ProjectionCheckpoint")}
                WHERE [ProjectionName] = @projectionName AND [ProjectionVersion] = @projectionVersion
                  AND [PartitionKey] = @partitionKey AND [LeaseOwner] = @owner AND [LeaseEpoch] = @epoch
                  AND [LeaseExpiresAtUtc] > SYSUTCDATETIME() AND [DataRevision] = @capturedRevision
            )
                THROW 51020, '{StatisticsContractConstants.Messages.MSG_RECONCILIATION_REVISION_STALE}', 1;
            """;
        AddPublishParameters(command, snapshot, lease, null);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task VerifyPublishedAsync(
        SqlProjectionSession session,
        ReconciliationSnapshot snapshot,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT CASE WHEN EXISTS
            (
                SELECT 1 FROM {Table("ProjectionCheckpoint")}
                WHERE [ProjectionName] = @projectionName AND [ProjectionVersion] = @projectionVersion
                  AND [PartitionKey] = @partitionKey AND [LeaseOwner] = @owner AND [LeaseEpoch] = @epoch
                  AND [DataRevision] = @nextRevision
            ) AND EXISTS
            (
                SELECT 1 FROM {Table("ReconciliationRequest")}
                WHERE [ReconciliationRequestId] = @requestId AND [Status] = 'Completed'
            ) THEN 1 ELSE 0 END;
            """;
        AddPublishParameters(command, snapshot, lease, null);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new ReconciliationStaleException();
        }
    }

    private async Task<IReadOnlyList<ReconciliationMembership>> ReadMembershipAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT [EventId], [SourceDocumentId]
            FROM {Table("ProcessedEvent")} WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProjectionName] = @projectionName AND [ProjectionVersion] = @projectionVersion
              AND [TimelineAtUtc] >= @fromUtc AND [TimelineAtUtc] < @toUtc;
            """;
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@fromUtc", from.UtcDateTime));
        command.Parameters.Add(new SqlParameter("@toUtc", to.UtcDateTime));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ReconciliationMembership>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReconciliationMembership(
                Convert.ToHexString((byte[])reader[0]).ToLowerInvariant(),
                reader.GetString(1)));
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<StateStreamKey, StateCursorSnapshot>> ReadOpeningCursorAsync(
        SqlProjectionSession session,
        ReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT TOP (1) [ClosingConnectionStatus], [BucketEndAtUtc], [OpeningEvidenceEventId]
            FROM {Table("DeviceStateDaily")} WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProjectionVersion] = @projectionVersion AND [CompanyId] = @companyId AND [DeviceId] = @deviceId
              AND [StateType] = @stateType AND [StatisticsDate] < @fromDate
              AND [IsDirty] = 0 AND [CoverageStatus] = 'complete'
            ORDER BY [StatisticsDate] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@projectionVersion", request.Identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@companyId", request.Key.CompanyId));
        command.Parameters.Add(new SqlParameter("@deviceId", request.Key.DeviceId));
        command.Parameters.Add(new SqlParameter("@stateType", request.Key.StateType));
        command.Parameters.Add(new SqlParameter("@fromDate", request.FromStatisticsDate.ToDateTime(TimeOnly.MinValue)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new Dictionary<StateStreamKey, StateCursorSnapshot>();
        }

        var bucketStart = BucketStart(request.FromStatisticsDate);
        var state = reader.GetString(0);
        var cursor = new StateCursorSnapshot(
            request.Key,
            state,
            bucketStart,
            bucketStart,
            bucketStart,
            new string('0', 64),
            StateEvidenceKinds.CarriedForward);
        return new Dictionary<StateStreamKey, StateCursorSnapshot> { [request.Key] = cursor };
    }

    private async Task<long> ReadRevisionAsync(
        SqlProjectionSession session,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT [DataRevision] FROM {Table("ProjectionCheckpoint")}
            WHERE [ProjectionName] = @projectionName AND [ProjectionVersion] = @projectionVersion
              AND [PartitionKey] = @partitionKey AND [LeaseOwner] = @owner AND [LeaseEpoch] = @epoch;
            """;
        AddLeaseParameters(command, lease);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task DeleteStagingAsync(
        SqlProjectionSession session,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            session,
            $"""
            DELETE FROM {Table("ProjectionStagingEvent")} WHERE [RunId] = @runId;
            DELETE FROM {Table("ProjectionStagingDaily")} WHERE [RunId] = @runId;
            DELETE FROM {Table("ProjectionStagingSummary")} WHERE [RunId] = @runId;
            DELETE FROM {Table("ProjectionStagingState")} WHERE [RunId] = @runId;
            DELETE FROM {Table("ProjectionStagingCoverage")} WHERE [RunId] = @runId;
            DELETE FROM {Table("ProjectionStagingQuality")} WHERE [RunId] = @runId;
            DELETE FROM {Table("ProjectionStagingCursor")} WHERE [RunId] = @runId;
            """,
            command => command.Parameters.Add(new SqlParameter("@runId", runId)),
            cancellationToken);
    }

    private async Task BulkCopyAsync(
        SqlProjectionSession session,
        string tableName,
        DataTable table,
        CancellationToken cancellationToken)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        using var copy = new SqlBulkCopy(
            session.Connection,
            SqlBulkCopyOptions.CheckConstraints,
            session.Transaction)
        {
            DestinationTableName = Table(tableName),
            BulkCopyTimeout = options.CommandTimeoutSeconds
        };
        foreach (DataColumn column in table.Columns)
        {
            copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await copy.WriteToServerAsync(table, cancellationToken);
    }

    private DataTable CreateEventTable(
        ReconciliationSnapshot snapshot,
        ReconciliationSourceResult result)
    {
        var table = CreateTable(
            ("RunId", typeof(Guid)), ("EventId", typeof(byte[])), ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)), ("StatisticsDate", typeof(DateTime)), ("Outcome", typeof(string)),
            ("CreatedAtUtc", typeof(DateTime)));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var value in result.ProcessedEvents)
        {
            var row = table.NewRow();
            row.ItemArray =
            [snapshot.RunId, Bytes(value.EventId), DBNull.Value, DBNull.Value,
                value.StatisticsDate is DateOnly statisticsDate
                    ? statisticsDate.ToDateTime(TimeOnly.MinValue)
                    : DBNull.Value, Outcome(value.Outcome), now];
            table.Rows.Add(row);
        }

        return table;
    }

    private DataTable CreateMetricTable(ReconciliationSnapshot snapshot, ReconciliationSourceResult result)
    {
        var table = CreateTable(
            ("RunId", typeof(Guid)), ("ProjectionVersion", typeof(int)), ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)), ("StatisticsDate", typeof(DateTime)), ("MetricKey", typeof(int)),
            ("SourceKind", typeof(string)), ("EventCount", typeof(long)), ("FirstEventAtUtc", typeof(DateTime)),
            ("LastEventAtUtc", typeof(DateTime)), ("CreatedAtUtc", typeof(DateTime)),
            ("ParsedWithWarningsCount", typeof(long)), ("OccurredTimeBasisCount", typeof(long)),
            ("ReceivedTimeBasisCount", typeof(long)), ("LastSourcePersistedAtUtc", typeof(DateTime)));
        foreach (var group in result.MetricContributions.GroupBy(value => new
                     { value.CompanyId, value.DeviceId, value.StatisticsDate, value.MetricKey, value.SourceKind }))
        {
            var row = table.NewRow();
            var values = group.ToArray();
            row.ItemArray =
            [snapshot.RunId, snapshot.Claim.Request.Identity.ProjectionVersion, group.Key.CompanyId, group.Key.DeviceId,
                group.Key.StatisticsDate.ToDateTime(TimeOnly.MinValue), group.Key.MetricKey, group.Key.SourceKind, (long)values.Length,
                values.Min(value => value.TimelineAtUtc).UtcDateTime, values.Max(value => value.TimelineAtUtc).UtcDateTime,
                timeProvider.GetUtcNow().UtcDateTime, values.LongCount(value => value.ParsedWithWarnings),
                values.LongCount(value => value.TimeBasis == EventTimeBasis.Occurred),
                values.LongCount(value => value.TimeBasis == EventTimeBasis.Received),
                values.Max(value => value.SourcePersistedAtUtc).UtcDateTime];
            table.Rows.Add(row);
        }

        return table;
    }

    private DataTable CreateSummaryTable(ReconciliationSnapshot snapshot, ReconciliationSourceResult result)
    {
        var table = CreateTable(
            ("RunId", typeof(Guid)), ("ProjectionVersion", typeof(int)), ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)), ("StatisticsDate", typeof(DateTime)), ("EventCount", typeof(long)),
            ("ErrorEventCount", typeof(long)), ("WarningEventCount", typeof(long)),
            ("FirstEventAtUtc", typeof(DateTime)), ("LastEventAtUtc", typeof(DateTime)), ("CreatedAtUtc", typeof(DateTime)));
        foreach (var group in result.DeviceSummaries.GroupBy(value => new { value.CompanyId, value.DeviceId, value.StatisticsDate }))
        {
            var values = group.ToArray();
            var row = table.NewRow();
            row.ItemArray =
            [snapshot.RunId, snapshot.Claim.Request.Identity.ProjectionVersion, group.Key.CompanyId, group.Key.DeviceId,
                group.Key.StatisticsDate.ToDateTime(TimeOnly.MinValue), (long)values.Length,
                values.LongCount(value => value.IsError), values.LongCount(value => value.IsWarning),
                values.Min(value => value.TimelineAtUtc).UtcDateTime, values.Max(value => value.TimelineAtUtc).UtcDateTime,
                timeProvider.GetUtcNow().UtcDateTime];
            table.Rows.Add(row);
        }

        return table;
    }

    private DataTable CreateStateTable(ReconciliationSnapshot snapshot, ReconciliationSourceResult result)
    {
        var table = CreateTable(
            ("RunId", typeof(Guid)), ("ProjectionVersion", typeof(int)), ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)), ("StatisticsDate", typeof(DateTime)), ("StateType", typeof(string)),
            ("OpeningState", typeof(string)), ("ClosingState", typeof(string)), ("OnlineSeconds", typeof(long)),
            ("OfflineSeconds", typeof(long)), ("UnknownSeconds", typeof(long)), ("CreatedAtUtc", typeof(DateTime)),
            ("BucketStartAtUtc", typeof(DateTime)), ("BucketEndAtUtc", typeof(DateTime)),
            ("CalculatedThroughAtUtc", typeof(DateTime)), ("TimeZoneId", typeof(string)),
            ("ConnectedEventCount", typeof(long)), ("DisconnectedEventCount", typeof(long)), ("ReconnectCount", typeof(long)),
            ("OpeningEvidenceKind", typeof(string)), ("OpeningEvidenceEventId", typeof(byte[])), ("IsDirty", typeof(bool)),
            ("IsFinalized", typeof(bool)), ("CoverageStatus", typeof(string)));
        foreach (var value in result.StateDailyContributions)
        {
            var row = table.NewRow();
            row.ItemArray =
            [snapshot.RunId, snapshot.Claim.Request.Identity.ProjectionVersion, value.Key.CompanyId, value.Key.DeviceId,
                value.StatisticsDate.ToDateTime(TimeOnly.MinValue), value.Key.StateType, value.OpeningState, value.ClosingState,
                value.OnlineSeconds, value.OfflineSeconds, value.UnknownSeconds, timeProvider.GetUtcNow().UtcDateTime,
                value.BucketStartAtUtc.UtcDateTime, value.BucketEndAtUtc.UtcDateTime, value.CalculatedThroughAtUtc.UtcDateTime,
                value.TimeZoneId, value.ConnectedEventCount, value.DisconnectedEventCount, value.ReconnectCount,
                value.OpeningEvidenceKind, value.OpeningEvidenceEventId is null ? DBNull.Value : Bytes(value.OpeningEvidenceEventId),
                value.IsDirty, value.IsFinalized, value.CoverageStatus];
            table.Rows.Add(row);
        }

        return table;
    }

    private DataTable CreateCoverageTable(ReconciliationSnapshot snapshot, ReconciliationSourceResult result)
    {
        var table = CreateTable(
            ("RunId", typeof(Guid)), ("ProjectionName", typeof(string)), ("ProjectionVersion", typeof(int)),
            ("CompanyId", typeof(long)), ("DeviceId", typeof(long)), ("StatisticsDate", typeof(DateTime)),
            ("CoverageKind", typeof(string)), ("CoverageStatus", typeof(string)),
            ("CoveredFromAtUtc", typeof(DateTime)), ("CoveredThroughAtUtc", typeof(DateTime)),
            ("ReasonCode", typeof(string)), ("CreatedAtUtc", typeof(DateTime)));
        foreach (var value in result.Coverage)
        {
            var row = table.NewRow();
            row.ItemArray =
            [snapshot.RunId, snapshot.Claim.Request.Identity.ProjectionName, snapshot.Claim.Request.Identity.ProjectionVersion,
                value.CompanyId, value.DeviceId, value.StatisticsDate.ToDateTime(TimeOnly.MinValue), value.CoverageKind, value.CoverageStatus,
                value.CoveredFromAtUtc.UtcDateTime, value.CoveredThroughAtUtc.UtcDateTime,
                value.ReasonCode ?? (object)DBNull.Value, timeProvider.GetUtcNow().UtcDateTime];
            table.Rows.Add(row);
        }

        return table;
    }

    private DataTable CreateQualityTable(ReconciliationSnapshot snapshot, ReconciliationSourceResult result)
    {
        var table = CreateTable(
            ("RunId", typeof(Guid)), ("ProjectionVersion", typeof(int)), ("StatisticsDate", typeof(DateTime)),
            ("CompanyId", typeof(long)), ("SourceKind", typeof(string)), ("SourceId", typeof(string)),
            ("QualityCode", typeof(string)), ("EventCount", typeof(long)), ("FirstSeenAtUtc", typeof(DateTime)),
            ("LastSeenAtUtc", typeof(DateTime)), ("CreatedAtUtc", typeof(DateTime)));
        foreach (var group in result.QualityContributions.GroupBy(value => new
                     { value.StatisticsDate, value.CompanyId, value.SourceKind, value.SourceId, value.QualityCode }))
        {
            var values = group.ToArray();
            var row = table.NewRow();
            row.ItemArray =
            [snapshot.RunId, snapshot.Claim.Request.Identity.ProjectionVersion, group.Key.StatisticsDate.ToDateTime(TimeOnly.MinValue),
                group.Key.CompanyId, group.Key.SourceKind, group.Key.SourceId, group.Key.QualityCode, (long)values.Length,
                values.Min(value => value.SeenAtUtc).UtcDateTime, values.Max(value => value.SeenAtUtc).UtcDateTime,
                timeProvider.GetUtcNow().UtcDateTime];
            table.Rows.Add(row);
        }

        return table;
    }

    private DataTable CreateCursorTable(ReconciliationSnapshot snapshot, ReconciliationSourceResult result)
    {
        var table = CreateTable(
            ("RunId", typeof(Guid)), ("ProjectionVersion", typeof(int)), ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)), ("StateType", typeof(string)), ("CurrentState", typeof(string)),
            ("StateSinceAtUtc", typeof(DateTime)), ("AccountedThroughAtUtc", typeof(DateTime)),
            ("LastTimelineAtUtc", typeof(DateTime)), ("LastEventId", typeof(byte[])),
            ("OpeningEvidenceKind", typeof(string)), ("CreatedAtUtc", typeof(DateTime)));
        foreach (var value in result.StateCursors)
        {
            var row = table.NewRow();
            row.ItemArray =
            [snapshot.RunId, snapshot.Claim.Request.Identity.ProjectionVersion, value.Key.CompanyId, value.Key.DeviceId,
                value.Key.StateType, value.CurrentState, value.StateSinceAtUtc.UtcDateTime,
                value.AccountedThroughAtUtc.UtcDateTime, value.LastTimelineAtUtc.UtcDateTime, Bytes(value.LastEventId),
                value.OpeningEvidenceKind, timeProvider.GetUtcNow().UtcDateTime];
            table.Rows.Add(row);
        }

        return table;
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

    private static void AddPublishParameters(
        SqlCommand command,
        ReconciliationSnapshot snapshot,
        ProjectionLeaseToken lease,
        ReconciliationSourceResult? result)
    {
        var request = snapshot.Claim.Request;
        command.Parameters.Add(new SqlParameter("@runId", snapshot.RunId));
        command.Parameters.Add(new SqlParameter("@projectionName", request.Identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", request.Identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@partitionKey", request.Identity.PartitionKey));
        command.Parameters.Add(new SqlParameter("@companyId", request.Key.CompanyId));
        command.Parameters.Add(new SqlParameter("@deviceId", request.Key.DeviceId));
        command.Parameters.Add(new SqlParameter("@fromDate", request.FromStatisticsDate.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new SqlParameter("@toDate", request.ToStatisticsDate.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new SqlParameter("@requestId", request.RequestId));
        command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
        command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
        command.Parameters.Add(new SqlParameter("@dirtyGeneration", request.DirtyGeneration));
        command.Parameters.Add(new SqlParameter("@capturedRevision", snapshot.CapturedDataRevision));
        command.Parameters.Add(new SqlParameter("@nextRevision", snapshot.CapturedDataRevision + 1));
        command.Parameters.Add(new SqlParameter("@startedAtUtc", snapshot.Claim.Request.RequestedAtUtc.UtcDateTime));
        command.Parameters.Add(new SqlParameter("@readEventCount", result?.ReadEventCount ?? 0));
        command.Parameters.Add(new SqlParameter("@aggregatedEventCount", result?.ProcessedEvents.Count(value => value.Outcome == ProjectionEventDisposition.Aggregated) ?? 0));
        command.Parameters.Add(new SqlParameter("@ignoredEventCount", result?.ProcessedEvents.Count(value => value.Outcome == ProjectionEventDisposition.Ignored) ?? 0));
        command.Parameters.Add(new SqlParameter("@failureEventCount", result?.ProcessedEvents.Count(value => value.Outcome == ProjectionEventDisposition.FailedTerminal) ?? 0));
        command.Parameters.Add(new SqlParameter("@affectedRowCount", result is null ? 0 : result.MetricContributions.Count + result.DeviceSummaries.Count + result.StateDailyContributions.Count));
        command.Parameters.Add(new SqlParameter("@runType", GetRunType(request.ReasonCode)));
    }

    private static void AddLeaseParameters(SqlCommand command, ProjectionLeaseToken lease)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", lease.Identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", lease.Identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@partitionKey", lease.Identity.PartitionKey));
        command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
        command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
    }

    private static DataTable CreateTable(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column.Name, column.Type);
        }

        return table;
    }

    private static byte[] Bytes(string value) => Convert.FromHexString(value);

    private static string GetRunType(string reasonCode) => reasonCode switch
    {
        ReconciliationReasonCodes.Bootstrap => "bootstrap",
        ReconciliationReasonCodes.Backfill => "backfill",
        ReconciliationReasonCodes.Rebuild => "rebuild",
        _ => "reconciliation"
    };

    private static string Outcome(ProjectionEventDisposition value) => value switch
    {
        ProjectionEventDisposition.Aggregated => StatisticsContractConstants.Outcomes.Aggregated,
        ProjectionEventDisposition.Ignored => StatisticsContractConstants.Outcomes.Ignored,
        ProjectionEventDisposition.QualityOnly => StatisticsContractConstants.Outcomes.QualityOnly,
        ProjectionEventDisposition.FailedTerminal => StatisticsContractConstants.Outcomes.FailedTerminal,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static DateTimeOffset BucketStart(DateOnly date) =>
        new DateTimeOffset(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            TimeSpan.FromHours(7)).ToUniversalTime();

    private string Table(string name) =>
        StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, name);
}
