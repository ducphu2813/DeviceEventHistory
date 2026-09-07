IF OBJECT_ID(N'[__SCHEMA__].[ReconciliationRequest]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ReconciliationRequest]
    (
        [RequestId] bigint IDENTITY(1,1) NOT NULL,
        [ProjectionName] varchar(100) NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [FromStatisticsDate] date NOT NULL,
        [ToStatisticsDate] date NOT NULL,
        [ReasonCode] varchar(64) NOT NULL,
        [Status] varchar(32) NOT NULL CONSTRAINT [DF_ReconciliationRequest_Status] DEFAULT 'Pending',
        [AttemptCount] int NOT NULL CONSTRAINT [DF_ReconciliationRequest_AttemptCount] DEFAULT 0,
        [NextAttemptAtUtc] datetime2(7) NULL,
        [ClaimOwner] varchar(200) NULL,
        [ClaimEpoch] bigint NULL,
        [ClaimExpiresAtUtc] datetime2(7) NULL,
        [DirtyGeneration] bigint NOT NULL CONSTRAINT [DF_ReconciliationRequest_DirtyGeneration] DEFAULT 0,
        [RequestedAtUtc] datetime2(7) NOT NULL,
        [StartedAtUtc] datetime2(7) NULL,
        [CompletedAtUtc] datetime2(7) NULL,
        [ErrorSummary] nvarchar(1000) NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_ReconciliationRequest] PRIMARY KEY CLUSTERED ([RequestId]),
        CONSTRAINT [CK_ReconciliationRequest_Dates] CHECK ([FromStatisticsDate] <= [ToStatisticsDate]),
        CONSTRAINT [CK_ReconciliationRequest_Status] CHECK ([Status] IN ('Pending', 'Processing', 'Completed', 'Failed', 'Cancelled')),
        CONSTRAINT [CK_ReconciliationRequest_Attempts] CHECK ([AttemptCount] >= 0)
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionFailure]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionFailure]
    (
        [ProjectionFailureKey] bigint IDENTITY(1,1) NOT NULL,
        [FailureId] binary(32) NOT NULL,
        [ProjectionName] varchar(100) NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [EventId] binary(32) NULL,
        [SourceEventIdentity] varchar(256) NOT NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [SourceKind] varchar(64) NULL,
        [Category] varchar(64) NULL,
        [SourceEventName] varchar(128) NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [ErrorCode] varchar(100) NOT NULL,
        [ErrorStage] varchar(64) NOT NULL,
        [ErrorMessage] nvarchar(1000) NOT NULL,
        [Retryable] bit NOT NULL,
        [RetryCount] int NOT NULL,
        [FirstFailedAtUtc] datetime2(7) NOT NULL,
        [LastFailedAtUtc] datetime2(7) NOT NULL,
        [ResolvedAtUtc] datetime2(7) NULL,
        [Resolution] nvarchar(500) NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_ProjectionFailure] PRIMARY KEY CLUSTERED ([ProjectionFailureKey]),
        CONSTRAINT [CK_ProjectionFailure_RetryCount] CHECK ([RetryCount] >= 0),
        CONSTRAINT [CK_ProjectionFailure_Time] CHECK ([FirstFailedAtUtc] <= [LastFailedAtUtc])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[ProjectionFailure]') AND [name] = N'UX_ProjectionFailure_FailureId')
BEGIN
    CREATE UNIQUE INDEX [UX_ProjectionFailure_FailureId]
        ON [__SCHEMA__].[ProjectionFailure] ([ProjectionName], [ProjectionVersion], [FailureId]);
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionRun]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionRun]
    (
        [ProjectionRunId] uniqueidentifier NOT NULL,
        [ProjectionName] varchar(100) NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [RunType] varchar(32) NOT NULL,
        [RequestedFromDate] date NULL,
        [RequestedToDate] date NULL,
        [RequestedCompanyId] bigint NULL,
        [StartedAtUtc] datetime2(7) NOT NULL,
        [CompletedAtUtc] datetime2(7) NULL,
        [Status] varchar(32) NOT NULL,
        [ReadEventCount] bigint NOT NULL,
        [AggregatedEventCount] bigint NOT NULL,
        [DuplicateEventCount] bigint NOT NULL,
        [IgnoredEventCount] bigint NOT NULL,
        [FailureEventCount] bigint NOT NULL,
        [AffectedRowCount] bigint NOT NULL,
        [CapturedDataRevision] bigint NULL,
        [ErrorSummary] nvarchar(2000) NULL,
        CONSTRAINT [PK_ProjectionRun] PRIMARY KEY CLUSTERED ([ProjectionRunId]),
        CONSTRAINT [CK_ProjectionRun_Type] CHECK ([RunType] IN ('incremental', 'reconciliation', 'backfill', 'rebuild')),
        CONSTRAINT [CK_ProjectionRun_Status] CHECK ([Status] IN ('running', 'succeeded', 'failed', 'cancelled'))
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingEvent]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionStagingEvent]
    (
        [RunId] uniqueidentifier NOT NULL,
        [EventId] binary(32) NOT NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [Outcome] varchar(32) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProjectionStagingEvent] PRIMARY KEY CLUSTERED ([RunId], [EventId])
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionStagingDaily]
    (
        [RunId] uniqueidentifier NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [MetricKey] int NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [EventCount] bigint NOT NULL,
        [FirstEventAtUtc] datetime2(7) NULL,
        [LastEventAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProjectionStagingDaily] PRIMARY KEY CLUSTERED
            ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind])
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingState]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionStagingState]
    (
        [RunId] uniqueidentifier NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [OpeningState] varchar(64) NOT NULL,
        [ClosingState] varchar(64) NOT NULL,
        [OnlineSeconds] bigint NOT NULL,
        [OfflineSeconds] bigint NOT NULL,
        [UnknownSeconds] bigint NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_ProjectionStagingState] PRIMARY KEY CLUSTERED
            ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [StateType])
    );
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionProcessedEventType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionProcessedEventType] AS TABLE
    (
        [EventId] binary(32) NOT NULL PRIMARY KEY,
        [SourceDocumentId] varchar(256) NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [SourcePersistedAtUtc] datetime2(7) NOT NULL,
        [StatisticsDate] date NULL,
        [TimelineAtUtc] datetime2(7) NULL,
        [MappingVersion] varchar(64) NOT NULL,
        [Outcome] varchar(32) NOT NULL
    )');
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionMetricContributionType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionMetricContributionType] AS TABLE
    (
        [EventId] binary(32) NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [MetricKey] int NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [TimelineAtUtc] datetime2(7) NOT NULL,
        [SourcePersistedAtUtc] datetime2(7) NOT NULL,
        [ParsedWithWarnings] bit NOT NULL,
        [TimeBasis] varchar(16) NOT NULL,
        PRIMARY KEY ([EventId], [MetricKey], [SourceKind], [StatisticsDate])
    )');
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionDeviceSummaryType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionDeviceSummaryType] AS TABLE
    (
        [EventId] binary(32) NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [IsError] bit NOT NULL,
        [IsWarning] bit NOT NULL,
        [TimelineAtUtc] datetime2(7) NOT NULL,
        PRIMARY KEY ([EventId], [SourceKind], [StatisticsDate])
    )');
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionStateObservationType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionStateObservationType] AS TABLE
    (
        [EventId] binary(32) NOT NULL PRIMARY KEY,
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [ObservedState] varchar(64) NOT NULL,
        [TimelineAtUtc] datetime2(7) NOT NULL,
        [OpeningEvidenceKind] varchar(64) NULL
    )');
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionQualityContributionType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionQualityContributionType] AS TABLE
    (
        [EventId] binary(32) NOT NULL,
        [QualityIdentity] binary(32) NOT NULL,
        [StatisticsDate] date NOT NULL,
        [CompanyId] bigint NOT NULL,
        [SourceKind] varchar(64) NOT NULL,
        [SourceId] varchar(200) NOT NULL,
        [QualityCode] varchar(100) NOT NULL,
        [SeenAtUtc] datetime2(7) NOT NULL,
        PRIMARY KEY ([EventId], [QualityCode])
    )');
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionFailureType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionFailureType] AS TABLE
    (
        [FailureId] binary(32) NOT NULL PRIMARY KEY,
        [EventId] binary(32) NULL,
        [SourceEventIdentity] varchar(256) NOT NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [SourceKind] varchar(64) NULL,
        [Category] varchar(64) NULL,
        [SourceEventName] varchar(128) NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [ErrorCode] varchar(100) NOT NULL,
        [ErrorStage] varchar(64) NOT NULL,
        [ErrorMessage] nvarchar(1000) NOT NULL,
        [Retryable] bit NOT NULL,
        [RetryCount] int NOT NULL,
        [FirstFailedAtUtc] datetime2(7) NOT NULL,
        [LastFailedAtUtc] datetime2(7) NOT NULL
    )');
END;
