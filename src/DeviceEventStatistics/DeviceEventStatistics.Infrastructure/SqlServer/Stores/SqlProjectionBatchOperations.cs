using System.Data;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Infrastructure.SqlServer.Mapping;
using Microsoft.Data.SqlClient;

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

    public async Task<bool> AdvanceCheckpointAsync(
        SqlProjectionSession session,
        ProjectionCheckpoint expected,
        ProjectionCheckpoint next,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default) =>
        await checkpointStore.AdvanceAsync(session, expected, next, lease, cancellationToken);

    private async Task EnsureFencedAsync(
        SqlProjectionSession session,
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken)
    {
        if (identity != lease.Identity) throw new ArgumentException("STAT-LEASE-IDENTITY-MISMATCH: Lease identity does not match operation identity.");

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
            IF @result < 0 THROW 51000, 'STAT-LEASE-APPLOCK-UNAVAILABLE', 1;

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
                THROW 51001, 'STAT-LEASE-LOST', 1;
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

    private static string GetTypeName(string parameterName) => parameterName switch
    {
        "events" => "ProjectionProcessedEventType",
        "contributions" => "ProjectionMetricContributionType",
        "quality" => "ProjectionQualityContributionType",
        "failures" => "ProjectionFailureType",
        _ => throw new ArgumentOutOfRangeException(nameof(parameterName))
    };
}
