IF COL_LENGTH(N'[__SCHEMA__].[ProjectionStagingDaily]', N'ParsedWithWarningsCount') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[ProjectionStagingDaily]
        ADD [ParsedWithWarningsCount] bigint NOT NULL CONSTRAINT [DF_ProjectionStagingDaily_ParsedWarnings] DEFAULT 0,
            [OccurredTimeBasisCount] bigint NOT NULL CONSTRAINT [DF_ProjectionStagingDaily_OccurredBasis] DEFAULT 0,
            [ReceivedTimeBasisCount] bigint NOT NULL CONSTRAINT [DF_ProjectionStagingDaily_ReceivedBasis] DEFAULT 0,
            [LastSourcePersistedAtUtc] datetime2(7) NULL;
END;

IF COL_LENGTH(N'[__SCHEMA__].[ReconciliationRequest]', N'EvidenceEventId') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[ReconciliationRequest]
        ADD [EvidenceEventId] binary(32) NULL;
END;

IF COL_LENGTH(N'[__SCHEMA__].[ProjectionStagingState]', N'BucketStartAtUtc') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[ProjectionStagingState]
        ADD [BucketStartAtUtc] datetime2(7) NULL,
            [BucketEndAtUtc] datetime2(7) NULL,
            [CalculatedThroughAtUtc] datetime2(7) NULL,
            [TimeZoneId] nvarchar(100) NULL,
            [ConnectedEventCount] bigint NOT NULL CONSTRAINT [DF_ProjectionStagingState_Connected] DEFAULT 0,
            [DisconnectedEventCount] bigint NOT NULL CONSTRAINT [DF_ProjectionStagingState_Disconnected] DEFAULT 0,
            [ReconnectCount] bigint NOT NULL CONSTRAINT [DF_ProjectionStagingState_Reconnect] DEFAULT 0,
            [OpeningEvidenceKind] varchar(64) NULL,
            [OpeningEvidenceEventId] binary(32) NULL,
            [IsDirty] bit NOT NULL CONSTRAINT [DF_ProjectionStagingState_Dirty] DEFAULT 0,
            [IsFinalized] bit NOT NULL CONSTRAINT [DF_ProjectionStagingState_Finalized] DEFAULT 0,
            [CoverageStatus] varchar(32) NULL;
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingSummary]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionStagingSummary]
    (
        [RunId] uniqueidentifier NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [EventCount] bigint NOT NULL,
        [ErrorEventCount] bigint NOT NULL,
        [WarningEventCount] bigint NOT NULL,
        [FirstEventAtUtc] datetime2(7) NULL,
        [LastEventAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProjectionStagingSummary] PRIMARY KEY CLUSTERED
            ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate])
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingCoverage]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionStagingCoverage]
    (
        [RunId] uniqueidentifier NOT NULL,
        [ProjectionName] varchar(100) NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [CoverageKind] varchar(64) NOT NULL,
        [CoverageStatus] varchar(32) NOT NULL,
        [CoveredFromAtUtc] datetime2(7) NOT NULL,
        [CoveredThroughAtUtc] datetime2(7) NOT NULL,
        [ReasonCode] varchar(100) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProjectionStagingCoverage] PRIMARY KEY CLUSTERED
            ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [CoverageKind])
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingQuality]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionStagingQuality]
    (
        [RunId] uniqueidentifier NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [StatisticsDate] date NOT NULL,
        [CompanyId] bigint NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [SourceId] varchar(200) NOT NULL,
        [QualityCode] varchar(100) NOT NULL,
        [EventCount] bigint NOT NULL,
        [FirstSeenAtUtc] datetime2(7) NOT NULL,
        [LastSeenAtUtc] datetime2(7) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProjectionStagingQuality] PRIMARY KEY CLUSTERED
            ([RunId], [ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode])
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingCursor]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionStagingCursor]
    (
        [RunId] uniqueidentifier NOT NULL,
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
        [CreatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProjectionStagingCursor] PRIMARY KEY CLUSTERED
            ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StateType])
    );
END;
