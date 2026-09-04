using System.Data;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Infrastructure.SqlServer.Mapping;
using Microsoft.Data.SqlClient;
using DeviceEventStatistics.Domain.State;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionBatchOperations(
    SqlStatisticsDatabaseOptions options,
    ProjectionTvpMapper mapper,
    SqlProjectionCheckpointStore checkpointStore)
{
    public async Task<IReadOnlySet<string>> InsertNewProcessedEventsAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<ProcessedEventInput> events,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        await EnsureFencedAsync(session, identity, lease, cancellationToken);

        await ExecuteAsync(
            session,
            $"""
            IF OBJECT_ID('tempdb..#StatisticsNewEvents') IS NULL
                CREATE TABLE #StatisticsNewEvents ([EventId] binary(32) NOT NULL PRIMARY KEY);

            DECLARE @newEvents TABLE ([EventId] binary(32) NOT NULL PRIMARY KEY);

            INSERT INTO {Table("ProcessedEvent")}
            (
                [ProjectionName], [ProjectionVersion], [EventId], [SourceDocumentId], [SourceKind],
                [SourcePersistedAtUtc], [TimelineAtUtc], [StatisticsDate], [MappingVersion],
                [Outcome], [ProcessedAtUtc]
            )
            OUTPUT INSERTED.[EventId] INTO @newEvents
            SELECT @projectionName, @projectionVersion, input.[EventId], input.[SourceDocumentId],
                   input.[SourceKind], input.[SourcePersistedAtUtc], input.[TimelineAtUtc],
                   input.[StatisticsDate], input.[MappingVersion], input.[Outcome], SYSUTCDATETIME()
            FROM @events input
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("ProcessedEvent")} existing WITH (UPDLOCK, HOLDLOCK)
                WHERE existing.[ProjectionName] = @projectionName
                  AND existing.[ProjectionVersion] = @projectionVersion
                  AND existing.[EventId] = input.[EventId]
            );

            INSERT INTO #StatisticsNewEvents ([EventId])
            SELECT [EventId] FROM @newEvents;
            """,
            command => AddProjectionParameters(command, identity, mapper.MapProcessedEvents(events), "events"),
            cancellationToken);

        return await ReadNewEventIdsAsync(session, cancellationToken);
    }

    public async Task<int> UpsertMetricDailyAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<MetricContribution> contributions,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (contributions.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);

        var affectedRows = await ExecuteAsync(
            session,
            $"""
            DECLARE @grouped TABLE
            (
                [ProjectionVersion] int NOT NULL,
                [CompanyId] bigint NOT NULL,
                [DeviceId] bigint NOT NULL,
                [StatisticsDate] date NOT NULL,
                [MetricKey] int NOT NULL,
                [SourceKind] varchar(64) NOT NULL,
                [EventCount] bigint NOT NULL,
                [ParsedWithWarningsCount] bigint NOT NULL,
                [OccurredTimeBasisCount] bigint NOT NULL,
                [ReceivedTimeBasisCount] bigint NOT NULL,
                [FirstEventAtUtc] datetime2(7) NOT NULL,
                [LastEventAtUtc] datetime2(7) NOT NULL,
                [LastSourcePersistedAtUtc] datetime2(7) NOT NULL,
                PRIMARY KEY ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind])
            );

            INSERT INTO @grouped
            SELECT @projectionVersion, input.[CompanyId], input.[DeviceId], input.[StatisticsDate],
                   input.[MetricKey], input.[SourceKind], COUNT_BIG(*),
                   SUM(CONVERT(bigint, CASE WHEN input.[ParsedWithWarnings] = 1 THEN 1 ELSE 0 END)),
                   SUM(CONVERT(bigint, CASE WHEN input.[TimeBasis] = 'occurred' THEN 1 ELSE 0 END)),
                   SUM(CONVERT(bigint, CASE WHEN input.[TimeBasis] = 'received' THEN 1 ELSE 0 END)),
                   MIN(input.[TimelineAtUtc]), MAX(input.[TimelineAtUtc]), MAX(input.[SourcePersistedAtUtc])
            FROM @contributions input
            INNER JOIN #StatisticsNewEvents newEvents ON newEvents.[EventId] = input.[EventId]
            GROUP BY input.[CompanyId], input.[DeviceId], input.[StatisticsDate], input.[MetricKey], input.[SourceKind];

            UPDATE target
            SET [EventCount] = target.[EventCount] + source.[EventCount],
                [ParsedWithWarningsCount] = target.[ParsedWithWarningsCount] + source.[ParsedWithWarningsCount],
                [OccurredTimeBasisCount] = target.[OccurredTimeBasisCount] + source.[OccurredTimeBasisCount],
                [ReceivedTimeBasisCount] = target.[ReceivedTimeBasisCount] + source.[ReceivedTimeBasisCount],
                [FirstEventAtUtc] = IIF(target.[FirstEventAtUtc] < source.[FirstEventAtUtc], target.[FirstEventAtUtc], source.[FirstEventAtUtc]),
                [LastEventAtUtc] = IIF(target.[LastEventAtUtc] > source.[LastEventAtUtc], target.[LastEventAtUtc], source.[LastEventAtUtc]),
                [LastSourcePersistedAtUtc] = IIF(target.[LastSourcePersistedAtUtc] > source.[LastSourcePersistedAtUtc], target.[LastSourcePersistedAtUtc], source.[LastSourcePersistedAtUtc]),
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("DeviceEventDaily")} target
            INNER JOIN @grouped source
                ON source.[ProjectionVersion] = target.[ProjectionVersion]
               AND source.[CompanyId] = target.[CompanyId]
               AND source.[DeviceId] = target.[DeviceId]
               AND source.[StatisticsDate] = target.[StatisticsDate]
               AND source.[MetricKey] = target.[MetricKey]
               AND source.[SourceKind] = target.[SourceKind];

            INSERT INTO {Table("DeviceEventDaily")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind],
                [EventCount], [ParsedWithWarningsCount], [OccurredTimeBasisCount], [ReceivedTimeBasisCount],
                [FirstEventAtUtc], [LastEventAtUtc], [LastSourcePersistedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]
            )
            SELECT source.[ProjectionVersion], source.[CompanyId], source.[DeviceId], source.[StatisticsDate],
                   source.[MetricKey], source.[SourceKind], source.[EventCount], source.[ParsedWithWarningsCount],
                   source.[OccurredTimeBasisCount], source.[ReceivedTimeBasisCount], source.[FirstEventAtUtc],
                   source.[LastEventAtUtc], source.[LastSourcePersistedAtUtc], SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM @grouped source
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("DeviceEventDaily")} target WITH (UPDLOCK, HOLDLOCK)
                WHERE target.[ProjectionVersion] = source.[ProjectionVersion]
                  AND target.[CompanyId] = source.[CompanyId]
                  AND target.[DeviceId] = source.[DeviceId]
                  AND target.[StatisticsDate] = source.[StatisticsDate]
                  AND target.[MetricKey] = source.[MetricKey]
                  AND target.[SourceKind] = source.[SourceKind]
            );
            """,
            command => AddProjectionParameters(command, identity, mapper.MapMetricContributions(contributions), "contributions"),
            cancellationToken);

        return affectedRows;
    }

    public async Task<int> InsertFailuresAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<ProjectionFailureInput> failures,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (failures.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        return await ExecuteAsync(
            session,
            $"""
            INSERT INTO {Table("ProjectionFailure")}
            (
                [FailureId], [ProjectionName], [ProjectionVersion], [EventId], [SourceEventIdentity],
                [CompanyId], [DeviceId], [SourceKind], [Category], [SourceEventName],
                [SourcePersistedAtUtc], [ErrorCode], [ErrorStage], [ErrorMessage], [Retryable],
                [RetryCount], [FirstFailedAtUtc], [LastFailedAtUtc]
            )
            SELECT input.[FailureId], @projectionName, @projectionVersion, input.[EventId],
                   input.[SourceEventIdentity], input.[CompanyId], input.[DeviceId], input.[SourceKind],
                   input.[Category], input.[SourceEventName], input.[SourcePersistedAtUtc], input.[ErrorCode],
                   input.[ErrorStage], input.[ErrorMessage], input.[Retryable], input.[RetryCount],
                   input.[FirstFailedAtUtc], input.[LastFailedAtUtc]
            FROM @failures input
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("ProjectionFailure")} existing WITH (UPDLOCK, HOLDLOCK)
                WHERE existing.[ProjectionName] = @projectionName
                  AND existing.[ProjectionVersion] = @projectionVersion
                  AND existing.[FailureId] = input.[FailureId]
            );
            """,
            command => AddProjectionParameters(command, identity, mapper.MapFailures(failures), "failures"),
            cancellationToken);
    }

    public async Task<int> UpsertDeviceSummariesAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<DeviceSummaryContribution> contributions,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (contributions.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        return await ExecuteAsync(
            session,
            $"""
            DECLARE @grouped TABLE
            (
                [CompanyId] bigint NOT NULL,
                [DeviceId] bigint NOT NULL,
                [StatisticsDate] date NOT NULL,
                [EventCount] bigint NOT NULL,
                [ErrorEventCount] bigint NOT NULL,
                [WarningEventCount] bigint NOT NULL,
                [FirstEventAtUtc] datetime2(7) NOT NULL,
                [LastEventAtUtc] datetime2(7) NOT NULL,
                PRIMARY KEY ([CompanyId], [DeviceId], [StatisticsDate])
            );

            INSERT INTO @grouped
            SELECT input.[CompanyId], input.[DeviceId], input.[StatisticsDate], COUNT_BIG(*),
                   SUM(CONVERT(bigint, CASE WHEN input.[IsError] = 1 THEN 1 ELSE 0 END)),
                   SUM(CONVERT(bigint, CASE WHEN input.[IsWarning] = 1 THEN 1 ELSE 0 END)),
                   MIN(input.[TimelineAtUtc]), MAX(input.[TimelineAtUtc])
            FROM @summaries input
            INNER JOIN #StatisticsNewEvents newEvents ON newEvents.[EventId] = input.[EventId]
            GROUP BY input.[CompanyId], input.[DeviceId], input.[StatisticsDate];

            UPDATE target
            SET [TotalEventCount] = target.[TotalEventCount] + source.[EventCount],
                [ErrorEventCount] = target.[ErrorEventCount] + source.[ErrorEventCount],
                [WarningEventCount] = target.[WarningEventCount] + source.[WarningEventCount],
                [FirstEventAtUtc] = CASE WHEN target.[FirstEventAtUtc] IS NULL OR target.[FirstEventAtUtc] > source.[FirstEventAtUtc]
                                         THEN source.[FirstEventAtUtc] ELSE target.[FirstEventAtUtc] END,
                [LastEventAtUtc] = CASE WHEN target.[LastEventAtUtc] IS NULL OR target.[LastEventAtUtc] < source.[LastEventAtUtc]
                                        THEN source.[LastEventAtUtc] ELSE target.[LastEventAtUtc] END,
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("DeviceDailySnapshot")} target
            INNER JOIN @grouped source
                ON source.[CompanyId] = target.[CompanyId]
               AND source.[DeviceId] = target.[DeviceId]
               AND source.[StatisticsDate] = target.[StatisticsDate]
            WHERE target.[ProjectionVersion] = @projectionVersion;

            INSERT INTO {Table("DeviceDailySnapshot")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [TimeZoneId],
                [BucketStartAtUtc], [BucketEndAtUtc], [OpeningConnectionStatus], [ClosingConnectionStatus],
                [ConnectedEventCount], [DisconnectedEventCount], [ReconnectCount], [TotalEventCount],
                [ErrorEventCount], [WarningEventCount], [FirstEventAtUtc], [LastEventAtUtc],
                [IsFinalized], [CalculatedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]
            )
            SELECT @projectionVersion, source.[CompanyId], source.[DeviceId], source.[StatisticsDate],
                   @timeZoneId,
                   DATEADD(HOUR, -7, CONVERT(datetime2(7), source.[StatisticsDate])),
                   DATEADD(DAY, 1, DATEADD(HOUR, -7, CONVERT(datetime2(7), source.[StatisticsDate]))),
                   'unknown', 'unknown', 0, 0, 0, source.[EventCount], source.[ErrorEventCount],
                   source.[WarningEventCount], source.[FirstEventAtUtc], source.[LastEventAtUtc],
                   0, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM @grouped source
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("DeviceDailySnapshot")} target WITH (UPDLOCK, HOLDLOCK)
                WHERE target.[ProjectionVersion] = @projectionVersion
                  AND target.[CompanyId] = source.[CompanyId]
                  AND target.[DeviceId] = source.[DeviceId]
                  AND target.[StatisticsDate] = source.[StatisticsDate]
            );
            """,
            command =>
            {
                AddProjectionParameters(command, identity, mapper.MapDeviceSummaries(contributions), "summaries");
                command.Parameters.Add(new SqlParameter("@timeZoneId", StatisticsContractConstants.DefaultTimeZoneId));
            },
            cancellationToken);
    }

    public async Task<int> UpsertQualityDailyAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<QualityContribution> contributions,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (contributions.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        return await ExecuteAsync(
            session,
            $"""
            DECLARE @grouped TABLE
            (
                [StatisticsDate] date NOT NULL,
                [CompanyId] bigint NOT NULL,
                [SourceKind] varchar(64) NOT NULL,
                [SourceId] varchar(200) NOT NULL,
                [QualityCode] varchar(100) NOT NULL,
                [EventCount] bigint NOT NULL,
                [FirstSeenAtUtc] datetime2(7) NOT NULL,
                [LastSeenAtUtc] datetime2(7) NOT NULL,
                PRIMARY KEY ([StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode])
            );

            INSERT INTO @grouped
            SELECT input.[StatisticsDate], input.[CompanyId], input.[SourceKind], input.[SourceId], input.[QualityCode],
                   COUNT_BIG(*), MIN(input.[SeenAtUtc]), MAX(input.[SeenAtUtc])
            FROM @quality input
            INNER JOIN #StatisticsNewEvents newEvents ON newEvents.[EventId] = input.[EventId]
            GROUP BY input.[StatisticsDate], input.[CompanyId], input.[SourceKind], input.[SourceId], input.[QualityCode];

            UPDATE target
            SET [EventCount] = target.[EventCount] + source.[EventCount],
                [FirstSeenAtUtc] = IIF(target.[FirstSeenAtUtc] < source.[FirstSeenAtUtc], target.[FirstSeenAtUtc], source.[FirstSeenAtUtc]),
                [LastSeenAtUtc] = IIF(target.[LastSeenAtUtc] > source.[LastSeenAtUtc], target.[LastSeenAtUtc], source.[LastSeenAtUtc]),
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("IngestionQualityDaily")} target
            INNER JOIN @grouped source
                ON source.[StatisticsDate] = target.[StatisticsDate]
               AND source.[CompanyId] = target.[CompanyId]
               AND source.[SourceKind] = target.[SourceKind]
               AND source.[SourceId] = target.[SourceId]
               AND source.[QualityCode] = target.[QualityCode]
            WHERE target.[ProjectionVersion] = @projectionVersion;

            INSERT INTO {Table("IngestionQualityDaily")}
            (
                [ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId],
                [QualityCode], [EventCount], [FirstSeenAtUtc], [LastSeenAtUtc], [UpdatedAtUtc]
            )
            SELECT @projectionVersion, source.[StatisticsDate], source.[CompanyId], source.[SourceKind],
                   source.[SourceId], source.[QualityCode], source.[EventCount], source.[FirstSeenAtUtc],
                   source.[LastSeenAtUtc], SYSUTCDATETIME()
            FROM @grouped source
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("IngestionQualityDaily")} target WITH (UPDLOCK, HOLDLOCK)
                WHERE target.[ProjectionVersion] = @projectionVersion
                  AND target.[StatisticsDate] = source.[StatisticsDate]
                  AND target.[CompanyId] = source.[CompanyId]
                  AND target.[SourceKind] = source.[SourceKind]
                  AND target.[SourceId] = source.[SourceId]
                  AND target.[QualityCode] = source.[QualityCode]
            );
            """,
            command => AddProjectionParameters(command, identity, mapper.MapQualityContributions(contributions), "quality"),
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<StateStreamKey, StateCursorSnapshot>> LoadStateCursorsAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<StateObservationInput> observations,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (observations.Count == 0)
        {
            return new Dictionary<StateStreamKey, StateCursorSnapshot>();
        }

        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = $"""
            SELECT c.[CompanyId], c.[DeviceId], c.[StateType], c.[CurrentState],
                   c.[StateSinceAtUtc], c.[AccountedThroughAtUtc], c.[LastTimelineAtUtc],
                   c.[LastEventId], c.[OpeningEvidenceKind]
            FROM {Table("DeviceStateCursor")} c WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN @stateObservations input
                ON input.[CompanyId] = c.[CompanyId]
               AND input.[DeviceId] = c.[DeviceId]
               AND input.[StateType] = c.[StateType]
            WHERE c.[ProjectionVersion] = @projectionVersion
            GROUP BY c.[CompanyId], c.[DeviceId], c.[StateType], c.[CurrentState],
                     c.[StateSinceAtUtc], c.[AccountedThroughAtUtc], c.[LastTimelineAtUtc],
                     c.[LastEventId], c.[OpeningEvidenceKind];
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddIdentityParameters(command, identity);
        var parameter = command.Parameters.Add("@stateObservations", System.Data.SqlDbType.Structured);
        parameter.TypeName = $"{options.SchemaName}.ProjectionStateObservationType";
        parameter.Value = mapper.MapStateObservations(observations);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<StateStreamKey, StateCursorSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = new StateStreamKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2));
            result[key] = new StateCursorSnapshot(
                key,
                reader.GetString(3),
                ReadDateTimeOffset(reader, 4),
                ReadDateTimeOffset(reader, 5),
                ReadDateTimeOffset(reader, 6),
                Convert.ToHexString((byte[])reader[7]).ToLowerInvariant(),
                reader.GetString(8));
        }

        return result;
    }

    public async Task<IReadOnlyList<StateCursorSnapshot>> LoadAllStateCursorsAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = $"""
            SELECT TOP (@maxCount) [CompanyId], [DeviceId], [StateType], [CurrentState],
                   [StateSinceAtUtc], [AccountedThroughAtUtc], [LastTimelineAtUtc], [LastEventId], [OpeningEvidenceKind]
            FROM {Table("DeviceStateCursor")} WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProjectionVersion] = @projectionVersion
            ORDER BY [AccountedThroughAtUtc], [CompanyId], [DeviceId], [StateType];
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddIdentityParameters(command, identity);
        command.Parameters.Add(new SqlParameter("@maxCount", maxCount));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StateCursorSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = new StateStreamKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2));
            result.Add(new StateCursorSnapshot(
                key,
                reader.GetString(3),
                ReadDateTimeOffset(reader, 4),
                ReadDateTimeOffset(reader, 5),
                ReadDateTimeOffset(reader, 6),
                Convert.ToHexString((byte[])reader[7]).ToLowerInvariant(),
                reader.GetString(8)));
        }

        return result;
    }

    public async Task<int> UpsertStateDailyAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<StateDailyContribution> changes,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (changes.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        return await ExecuteAsync(
            session,
            $"""
            UPDATE target
            SET [OnlineSeconds] = target.[OnlineSeconds] + input.[OnlineSeconds],
                [OfflineSeconds] = target.[OfflineSeconds] + input.[OfflineSeconds],
                [UnknownSeconds] = target.[UnknownSeconds] + input.[UnknownSeconds],
                [ConnectedEventCount] = target.[ConnectedEventCount] + input.[ConnectedEventCount],
                [DisconnectedEventCount] = target.[DisconnectedEventCount] + input.[DisconnectedEventCount],
                [ReconnectCount] = target.[ReconnectCount] + input.[ReconnectCount],
                [ClosingConnectionStatus] = input.[ClosingState],
                [CalculatedThroughAtUtc] = CASE WHEN target.[CalculatedThroughAtUtc] > input.[CalculatedThroughAtUtc]
                                                THEN target.[CalculatedThroughAtUtc] ELSE input.[CalculatedThroughAtUtc] END,
                [IsDirty] = CONVERT(bit, CASE WHEN target.[IsDirty] = 1 OR input.[IsDirty] = 1 THEN 1 ELSE 0 END),
                [IsFinalized] = CONVERT(bit, CASE WHEN input.[IsDirty] = 1 THEN 0 ELSE input.[IsFinalized] END),
                [CoverageStatus] = input.[CoverageStatus],
                [CalculatedAtUtc] = SYSUTCDATETIME(),
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("DeviceStateDaily")} target
            INNER JOIN @stateDaily input
                ON input.[CompanyId] = target.[CompanyId]
               AND input.[DeviceId] = target.[DeviceId]
               AND input.[StatisticsDate] = target.[StatisticsDate]
               AND input.[StateType] = target.[StateType]
            WHERE target.[ProjectionVersion] = @projectionVersion;

            INSERT INTO {Table("DeviceStateDaily")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [StateType],
                [BucketStartAtUtc], [BucketEndAtUtc], [CalculatedThroughAtUtc], [OpeningConnectionStatus],
                [ClosingConnectionStatus], [OnlineSeconds], [OfflineSeconds], [UnknownSeconds],
                [ConnectedEventCount], [DisconnectedEventCount], [ReconnectCount], [OpeningEvidenceKind],
                [OpeningEvidenceEventId], [IsDirty], [IsFinalized], [CoverageStatus], [CalculatedAtUtc],
                [CreatedAtUtc], [UpdatedAtUtc]
            )
            SELECT @projectionVersion, input.[CompanyId], input.[DeviceId], input.[StatisticsDate], input.[StateType],
                   input.[BucketStartAtUtc], input.[BucketEndAtUtc], input.[CalculatedThroughAtUtc], input.[OpeningState],
                   input.[ClosingState], input.[OnlineSeconds], input.[OfflineSeconds], input.[UnknownSeconds],
                   input.[ConnectedEventCount], input.[DisconnectedEventCount], input.[ReconnectCount],
                   input.[OpeningEvidenceKind], input.[OpeningEvidenceEventId], input.[IsDirty], input.[IsFinalized],
                   input.[CoverageStatus], SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM @stateDaily input
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("DeviceStateDaily")} target WITH (UPDLOCK, HOLDLOCK)
                WHERE target.[ProjectionVersion] = @projectionVersion
                  AND target.[CompanyId] = input.[CompanyId]
                  AND target.[DeviceId] = input.[DeviceId]
                  AND target.[StatisticsDate] = input.[StatisticsDate]
                  AND target.[StateType] = input.[StateType]
            );
            """,
            command => AddProjectionParameters(command, identity, mapper.MapStateDailyContributions(changes), "stateDaily"),
            cancellationToken);
    }

    public async Task<int> UpsertStateCursorsAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<StateCursorInput> cursors,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (cursors.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        return await ExecuteAsync(
            session,
            $"""
            UPDATE target
            SET [CurrentState] = input.[CurrentState],
                [StateSinceAtUtc] = input.[StateSinceAtUtc],
                [AccountedThroughAtUtc] = input.[AccountedThroughAtUtc],
                [LastTimelineAtUtc] = input.[LastTimelineAtUtc],
                [LastEventId] = input.[LastEventId],
                [OpeningEvidenceKind] = input.[OpeningEvidenceKind],
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM {Table("DeviceStateCursor")} target
            INNER JOIN @stateCursors input
                ON input.[CompanyId] = target.[CompanyId]
               AND input.[DeviceId] = target.[DeviceId]
               AND input.[StateType] = target.[StateType]
            WHERE target.[ProjectionVersion] = @projectionVersion;

            INSERT INTO {Table("DeviceStateCursor")}
            (
                [ProjectionVersion], [CompanyId], [DeviceId], [StateType], [CurrentState], [StateSinceAtUtc],
                [AccountedThroughAtUtc], [LastTimelineAtUtc], [LastEventId], [OpeningEvidenceKind], [UpdatedAtUtc]
            )
            SELECT @projectionVersion, input.[CompanyId], input.[DeviceId], input.[StateType], input.[CurrentState],
                   input.[StateSinceAtUtc], input.[AccountedThroughAtUtc], input.[LastTimelineAtUtc], input.[LastEventId],
                   input.[OpeningEvidenceKind], SYSUTCDATETIME()
            FROM @stateCursors input
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("DeviceStateCursor")} target WITH (UPDLOCK, HOLDLOCK)
                WHERE target.[ProjectionVersion] = @projectionVersion
                  AND target.[CompanyId] = input.[CompanyId]
                  AND target.[DeviceId] = input.[DeviceId]
                  AND target.[StateType] = input.[StateType]
            );
            """,
            command => AddProjectionParameters(command, identity, mapper.MapStateCursors(cursors), "stateCursors"),
            cancellationToken);
    }

    public async Task<int> MarkStateDaysDirtyAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<StateDirtyRange> ranges,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (ranges.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        var count = 0;
        foreach (var range in ranges)
        {
            count += await ExecuteAsync(
                session,
                $"""
                UPDATE {Table("DeviceStateDaily")}
                SET [IsDirty] = 1, [IsFinalized] = 0, [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [ProjectionVersion] = @projectionVersion
                  AND [CompanyId] = @companyId AND [DeviceId] = @deviceId AND [StateType] = @stateType
                  AND [StatisticsDate] BETWEEN @fromDate AND @toDate;
                """,
                command =>
                {
                    AddIdentityParameters(command, identity);
                    command.Parameters.Add(new SqlParameter("@companyId", range.Key.CompanyId));
                    command.Parameters.Add(new SqlParameter("@deviceId", range.Key.DeviceId));
                    command.Parameters.Add(new SqlParameter("@stateType", range.Key.StateType));
                    command.Parameters.Add(new SqlParameter("@fromDate", range.FromStatisticsDate.ToDateTime()));
                    command.Parameters.Add(new SqlParameter("@toDate", range.ToStatisticsDate.ToDateTime()));
                },
                cancellationToken);
        }

        return count;
    }

    public async Task<int> UpsertReconciliationRequestsAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        IReadOnlyCollection<ReconciliationRequestInput> requests,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0) return 0;
        await EnsureFencedAsync(session, identity, lease, cancellationToken);
        return await ExecuteAsync(
            session,
            $"""
            UPDATE target
            SET [FromStatisticsDate] = CASE WHEN target.[FromStatisticsDate] < input.[FromStatisticsDate]
                                            THEN target.[FromStatisticsDate] ELSE input.[FromStatisticsDate] END,
                [ToStatisticsDate] = CASE WHEN target.[ToStatisticsDate] > input.[ToStatisticsDate]
                                          THEN target.[ToStatisticsDate] ELSE input.[ToStatisticsDate] END,
                [ReasonCode] = input.[ReasonCode], [Status] = 'Pending', [RequestedAtUtc] = input.[RequestedAtUtc],
                [ErrorSummary] = NULL, [DirtyGeneration] = target.[DirtyGeneration] + 1
            FROM {Table("ReconciliationRequest")} target
            INNER JOIN @reconciliation input
                ON input.[CompanyId] = target.[CompanyId]
               AND input.[DeviceId] = target.[DeviceId]
               AND input.[StateType] = target.[StateType]
               AND target.[ProjectionName] = @projectionName
               AND target.[ProjectionVersion] = @projectionVersion
               AND target.[Status] IN ('Pending', 'Processing');

            INSERT INTO {Table("ReconciliationRequest")}
            (
                [ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StateType],
                [FromStatisticsDate], [ToStatisticsDate], [ReasonCode], [Status], [RequestedAtUtc]
            )
            SELECT @projectionName, @projectionVersion, input.[CompanyId], input.[DeviceId], input.[StateType],
                   input.[FromStatisticsDate], input.[ToStatisticsDate], input.[ReasonCode], 'Pending', input.[RequestedAtUtc]
            FROM @reconciliation input
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {Table("ReconciliationRequest")} target WITH (UPDLOCK, HOLDLOCK)
                WHERE target.[ProjectionName] = @projectionName
                  AND target.[ProjectionVersion] = @projectionVersion
                  AND target.[CompanyId] = input.[CompanyId]
                  AND target.[DeviceId] = input.[DeviceId]
                  AND target.[StateType] = input.[StateType]
                  AND target.[Status] IN ('Pending', 'Processing')
            );
            """,
            command => AddProjectionParameters(command, identity, mapper.MapReconciliationRequests(requests), "reconciliation"),
            cancellationToken);
    }

    public async Task<bool> AdvanceCheckpointAsync(
        SqlProjectionSession session,
        ProjectionCheckpoint expected,
        ProjectionCheckpoint next,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default) =>
        await checkpointStore.AdvanceAsync(session, expected, next, lease, cancellationToken);

    internal async Task EnsureFencedAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        if (identity != lease.Identity)
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_SQL_LEASE_IDENTITY_MISMATCH);
        }

        await ExecuteAsync(
            session,
            $"""
            DECLARE @resource nvarchar(255) = @lockResource;
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 0;
            IF @result < 0 THROW 51000, '{StatisticsContractConstants.Messages.MSG_SQL_LEASE_APPLOCK_UNAVAILABLE}', 1;

            IF NOT EXISTS
            (
                SELECT 1
                FROM {Table("ProjectionCheckpoint")} WITH (UPDLOCK, HOLDLOCK)
                WHERE [ProjectionName] = @projectionName
                  AND [ProjectionVersion] = @projectionVersion
                  AND [PartitionKey] = @partitionKey
                  AND [LeaseOwner] = @owner
                  AND [LeaseEpoch] = @epoch
                  AND [LeaseExpiresAtUtc] > SYSUTCDATETIME()
            )
                THROW 51001, '{StatisticsContractConstants.LeaseErrors.Lost}', 1;
            """,
            command =>
            {
                AddIdentityParameters(command, identity);
                command.Parameters.Add(new SqlParameter("@owner", lease.Owner));
                command.Parameters.Add(new SqlParameter("@epoch", lease.Epoch));
                command.Parameters.Add(new SqlParameter("@lockResource", $"{identity.ProjectionName}:{identity.ProjectionVersion}:{identity.PartitionKey}"));
            },
            cancellationToken);
    }

    private async Task<int> ExecuteAsync(
        SqlProjectionSession session,
        string sql,
        Action<SqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlySet<string>> ReadNewEventIdsAsync(
        SqlProjectionSession session,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = "SELECT [EventId] FROM #StatisticsNewEvents;";
        command.CommandTimeout = options.CommandTimeoutSeconds;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(Convert.ToHexString((byte[])reader[0]).ToLowerInvariant());
        }

        return result;
    }

    private void AddProjectionParameters(
        SqlCommand command,
        ProjectionIdentity identity,
        DataTable table,
        string parameterName)
    {
        AddIdentityParameters(command, identity);
        var parameter = command.Parameters.Add($"@{parameterName}", SqlDbType.Structured);
        parameter.TypeName = $"{options.SchemaName}.{GetTypeName(parameterName)}";
        parameter.Value = table;
    }

    private static void AddIdentityParameters(SqlCommand command, ProjectionIdentity identity)
    {
        command.Parameters.Add(new SqlParameter("@projectionName", identity.ProjectionName));
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@partitionKey", identity.PartitionKey));
    }

    private string Table(string tableName) => $"{Quote(options.SchemaName)}.[{tableName}]";

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static DateTimeOffset ReadDateTimeOffset(SqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static string GetTypeName(string parameterName) => parameterName switch
    {
        "events" => "ProjectionProcessedEventType",
        "contributions" => "ProjectionMetricContributionType",
        "summaries" => "ProjectionDeviceSummaryType",
        "stateDaily" => "ProjectionStateDailyType",
        "stateCursors" => "ProjectionStateCursorType",
        "reconciliation" => "ProjectionReconciliationRequestType",
        "quality" => "ProjectionQualityContributionType",
        "failures" => "ProjectionFailureType",
        _ => throw new ArgumentOutOfRangeException(nameof(parameterName))
    };
}
