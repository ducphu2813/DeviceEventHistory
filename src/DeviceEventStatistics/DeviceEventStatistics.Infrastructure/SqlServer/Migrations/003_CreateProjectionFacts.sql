IF OBJECT_ID(N'[__SCHEMA__].[DeviceEventDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[DeviceEventDaily]
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
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_DeviceEventDaily] PRIMARY KEY CLUSTERED
            ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind]),
        CONSTRAINT [CK_DeviceEventDaily_Keys] CHECK ([ProjectionVersion] > 0 AND [CompanyId] > 0 AND [DeviceId] > 0),
        CONSTRAINT [CK_DeviceEventDaily_NonNegativeCounts] CHECK
            ([EventCount] >= 0 AND [ParsedWithWarningsCount] >= 0 AND [OccurredTimeBasisCount] >= 0 AND [ReceivedTimeBasisCount] >= 0),
        CONSTRAINT [CK_DeviceEventDaily_TimeBasisTotal] CHECK
            ([OccurredTimeBasisCount] + [ReceivedTimeBasisCount] = [EventCount]),
        CONSTRAINT [CK_DeviceEventDaily_EventRange] CHECK ([FirstEventAtUtc] <= [LastEventAtUtc])
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[DeviceDailySnapshot]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[DeviceDailySnapshot]
    (
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [BucketStartAtUtc] datetime2(7) NOT NULL,
        [BucketEndAtUtc] datetime2(7) NOT NULL,
        [OpeningConnectionStatus] varchar(32) NOT NULL,
        [ClosingConnectionStatus] varchar(32) NOT NULL,
        [ConnectedEventCount] bigint NOT NULL,
        [DisconnectedEventCount] bigint NOT NULL,
        [ReconnectCount] bigint NOT NULL,
        [TotalEventCount] bigint NOT NULL,
        [ErrorEventCount] bigint NOT NULL,
        [WarningEventCount] bigint NOT NULL,
        [FirstEventAtUtc] datetime2(7) NULL,
        [LastEventAtUtc] datetime2(7) NULL,
        [HealthStatus] varchar(32) NULL,
        [HealthScore] decimal(5,2) NULL,
        [HealthRuleVersion] int NULL,
        [HealthReasonJson] nvarchar(max) NULL,
        [IsFinalized] bit NOT NULL,
        [CalculatedAtUtc] datetime2(7) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_DeviceDailySnapshot] PRIMARY KEY CLUSTERED
            ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate]),
        CONSTRAINT [CK_DeviceDailySnapshot_Keys] CHECK ([ProjectionVersion] > 0 AND [CompanyId] > 0 AND [DeviceId] > 0),
        CONSTRAINT [CK_DeviceDailySnapshot_Bucket] CHECK ([BucketStartAtUtc] < [BucketEndAtUtc]),
        CONSTRAINT [CK_DeviceDailySnapshot_Status] CHECK
            ([OpeningConnectionStatus] IN ('connected', 'disconnected', 'unknown') AND
             [ClosingConnectionStatus] IN ('connected', 'disconnected', 'unknown')),
        CONSTRAINT [CK_DeviceDailySnapshot_Counts] CHECK
            ([ConnectedEventCount] >= 0 AND [DisconnectedEventCount] >= 0 AND [ReconnectCount] >= 0 AND
             [TotalEventCount] >= 0 AND [ErrorEventCount] >= 0 AND [WarningEventCount] >= 0),
        CONSTRAINT [CK_DeviceDailySnapshot_HealthScore] CHECK
            ([HealthScore] IS NULL OR ([HealthScore] >= 0 AND [HealthScore] <= 100)),
        CONSTRAINT [CK_DeviceDailySnapshot_HealthContract] CHECK
            (([HealthScore] IS NULL AND [HealthRuleVersion] IS NULL) OR
             ([HealthScore] IS NOT NULL AND [HealthRuleVersion] IS NOT NULL)),
        CONSTRAINT [CK_DeviceDailySnapshot_HealthReasonJson] CHECK
            ([HealthReasonJson] IS NULL OR ISJSON([HealthReasonJson]) = 1)
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[DeviceStateDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[DeviceStateDaily]
    (
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [BucketStartAtUtc] datetime2(7) NOT NULL,
        [BucketEndAtUtc] datetime2(7) NOT NULL,
        [CalculatedThroughAtUtc] datetime2(7) NOT NULL,
        [OpeningConnectionStatus] varchar(32) NOT NULL,
        [ClosingConnectionStatus] varchar(32) NOT NULL,
        [OnlineSeconds] bigint NOT NULL,
        [OfflineSeconds] bigint NOT NULL,
        [UnknownSeconds] bigint NOT NULL,
        [ConnectedEventCount] bigint NOT NULL,
        [DisconnectedEventCount] bigint NOT NULL,
        [ReconnectCount] bigint NOT NULL,
        [OpeningEvidenceKind] varchar(64) NOT NULL,
        [OpeningEvidenceEventId] binary(32) NULL,
        [IsDirty] bit NOT NULL,
        [IsFinalized] bit NOT NULL,
        [CoverageStatus] varchar(32) NOT NULL,
        [CalculatedAtUtc] datetime2(7) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_DeviceStateDaily] PRIMARY KEY CLUSTERED
            ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [StateType]),
        CONSTRAINT [CK_DeviceStateDaily_Bucket] CHECK
            ([BucketStartAtUtc] < [BucketEndAtUtc] AND [CalculatedThroughAtUtc] BETWEEN [BucketStartAtUtc] AND [BucketEndAtUtc]),
        CONSTRAINT [CK_DeviceStateDaily_Durations] CHECK
            ([OnlineSeconds] >= 0 AND [OfflineSeconds] >= 0 AND [UnknownSeconds] >= 0),
        CONSTRAINT [CK_DeviceStateDaily_DurationTotal] CHECK
            ([OnlineSeconds] + [OfflineSeconds] + [UnknownSeconds] =
             DATEDIFF_BIG(SECOND, [BucketStartAtUtc], [CalculatedThroughAtUtc])),
        CONSTRAINT [CK_DeviceStateDaily_Status] CHECK
            ([OpeningConnectionStatus] IN ('connected', 'disconnected', 'unknown') AND
             [ClosingConnectionStatus] IN ('connected', 'disconnected', 'unknown')),
        CONSTRAINT [CK_DeviceStateDaily_Coverage] CHECK
            ([CoverageStatus] IN ('complete', 'partial', 'unrecoverable'))
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[DeviceStateCursor]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[DeviceStateCursor]
    (
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [CurrentState] varchar(64) NOT NULL,
        [StateSinceAtUtc] datetime2(7) NOT NULL,
        [AccountedThroughAtUtc] datetime2(7) NOT NULL,
        [LastTimelineAtUtc] datetime2(7) NOT NULL,
        [LastEventId] binary(32) NOT NULL,
        [OpeningEvidenceKind] varchar(64) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_DeviceStateCursor] PRIMARY KEY CLUSTERED
            ([ProjectionVersion], [CompanyId], [DeviceId], [StateType]),
        CONSTRAINT [CK_DeviceStateCursor_EventOrder] CHECK ([StateSinceAtUtc] <= [LastTimelineAtUtc]),
        CONSTRAINT [CK_DeviceStateCursor_Accounting] CHECK ([AccountedThroughAtUtc] >= [StateSinceAtUtc])
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProcessedEvent]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProcessedEvent]
    (
        [ProcessedEventKey] bigint IDENTITY(1,1) NOT NULL,
        [ProjectionName] varchar(100) NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [EventId] binary(32) NOT NULL,
        [SourceDocumentId] varchar(256) NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [SourcePersistedAtUtc] datetime2(7) NOT NULL,
        [TimelineAtUtc] datetime2(7) NULL,
        [StatisticsDate] date NULL,
        [MappingVersion] varchar(64) NOT NULL,
        [Outcome] varchar(32) NOT NULL,
        [ProcessedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProcessedEvent] PRIMARY KEY CLUSTERED ([ProcessedEventKey]),
        CONSTRAINT [CK_ProcessedEvent_ProjectionVersion] CHECK ([ProjectionVersion] > 0),
        CONSTRAINT [CK_ProcessedEvent_Outcome] CHECK ([Outcome] IN ('aggregated', 'ignored', 'quality_only', 'failed_terminal'))
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[ProcessedEvent]')
      AND [name] = N'UX_ProcessedEvent_Projection_Event'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ProcessedEvent_Projection_Event]
        ON [__SCHEMA__].[ProcessedEvent] ([ProjectionName], [ProjectionVersion], [EventId]);
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionCheckpoint]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionCheckpoint]
    (
        [ProjectionName] varchar(100) NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [PartitionKey] varchar(100) NOT NULL,
        [LastPersistedAtUtc] datetime2(7) NULL,
        [LastEventId] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        [LastProcessedAtUtc] datetime2(7) NULL,
        [LastBatchSize] int NOT NULL,
        [SweepFromAtUtc] datetime2(7) NULL,
        [SweepToAtUtc] datetime2(7) NULL,
        [SweepLastPersistedAtUtc] datetime2(7) NULL,
        [SweepLastEventId] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        [LeaseOwner] varchar(200) NULL,
        [LeaseExpiresAtUtc] datetime2(7) NULL,
        [LeaseEpoch] bigint NOT NULL CONSTRAINT [DF_ProjectionCheckpoint_LeaseEpoch] DEFAULT 0,
        [DataRevision] bigint NOT NULL CONSTRAINT [DF_ProjectionCheckpoint_DataRevision] DEFAULT 0,
        [LastCompletedSweepAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_ProjectionCheckpoint] PRIMARY KEY CLUSTERED
            ([ProjectionName], [ProjectionVersion], [PartitionKey]),
        CONSTRAINT [CK_ProjectionCheckpoint_CursorPair] CHECK
            (([LastPersistedAtUtc] IS NULL AND [LastEventId] IS NULL) OR
             ([LastPersistedAtUtc] IS NOT NULL AND [LastEventId] IS NOT NULL)),
        CONSTRAINT [CK_ProjectionCheckpoint_SweepCursorPair] CHECK
            (([SweepLastPersistedAtUtc] IS NULL AND [SweepLastEventId] IS NULL) OR
             ([SweepLastPersistedAtUtc] IS NOT NULL AND [SweepLastEventId] IS NOT NULL)),
        CONSTRAINT [CK_ProjectionCheckpoint_BatchSize] CHECK ([LastBatchSize] >= 0)
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[IngestionQualityDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[IngestionQualityDaily]
    (
        [ProjectionVersion] int NOT NULL,
        [StatisticsDate] date NOT NULL,
        [CompanyId] bigint NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [SourceId] varchar(200) NOT NULL,
        [QualityCode] varchar(100) NOT NULL,
        [EventCount] bigint NOT NULL,
        [FirstSeenAtUtc] datetime2(7) NOT NULL,
        [LastSeenAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_IngestionQualityDaily] PRIMARY KEY CLUSTERED
            ([ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode]),
        CONSTRAINT [CK_IngestionQualityDaily_Count] CHECK ([EventCount] >= 0 AND [CompanyId] >= 0)
    );
END;
