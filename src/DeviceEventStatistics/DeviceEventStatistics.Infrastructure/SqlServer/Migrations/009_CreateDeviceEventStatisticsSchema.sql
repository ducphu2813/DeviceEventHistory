-- Device Event Statistics - standalone SQL Server bootstrap script
-- Execute this file in the database selected in SSMS or Azure Data Studio.
-- Physical table names use the DES.* convention under the dbo schema.

SET NOCOUNT ON;

IF OBJECT_ID(N'[dbo].[DES.SchemaMigration]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.SchemaMigration]
    (
        [SchemaMigrationId] INT IDENTITY(1, 1) PRIMARY KEY,
        [MigrationId] varchar(100) NULL,
        [Checksum] binary(32) NULL,
        [AppliedAtUtc] datetime2(7) NULL,
        [AppliedBy] varchar(128) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionDefinition]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionDefinition]
    (
        [ProjectionDefinitionId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [MappingVersion] varchar(64) NULL,
        [OwnershipVersion] varchar(64) NULL,
        [MetricSetVersion] int NULL,
        [CoverageStartAtUtc] datetime2(7) NULL,
        [TimeZoneId] nvarchar(100) NULL,
        [LifecycleStatus] varchar(32) NULL,
        [CreatedAtUtc] datetime2(7) NULL,
        [ActivatedAtUtc] datetime2(7) NULL,
        [RetiredAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.DeviceDimension]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.DeviceDimension]
    (
        [DeviceDimensionId] INT IDENTITY(1, 1) PRIMARY KEY,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [DeviceCode] nvarchar(100) NULL,
        [DeviceName] nvarchar(250) NULL,
        [DeviceType] varchar(64) NULL,
        [GateId] bigint NULL,
        [GateCode] nvarchar(100) NULL,
        [GateName] nvarchar(250) NULL,
        [TimeZoneId] nvarchar(100) NULL,
        [TimeZoneEffectiveFromUtc] datetime2(7) NULL,
        [IsActive] bit NULL,
        [MetadataSource] varchar(64) NULL,
        [MetadataUpdatedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.MetricDefinition]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.MetricDefinition]
    (
        [MetricDefinitionId] INT IDENTITY(1, 1) PRIMARY KEY,
        [MetricKey] int NULL,
        [MetricSetVersion] int NULL,
        [MetricCode] varchar(100) NULL,
        [DisplayName] nvarchar(250) NULL,
        [MetricGroup] varchar(64) NULL,
        [Unit] varchar(32) NULL,
        [DefaultCategory] varchar(64) NULL,
        [PrimarySourceKind] varchar(64) NULL,
        [IsHealthInput] bit NULL,
        [IsEnabled] bit NULL,
        [MappingVersion] varchar(64) NULL,
        [OwnershipVersion] varchar(64) NULL,
        [CreatedAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionCoverage]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionCoverage]
    (
        [ProjectionCoverageId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [CoverageKind] varchar(64) NULL,
        [CoverageStatus] varchar(32) NULL,
        [CoveredFromAtUtc] datetime2(7) NULL,
        [CoveredThroughAtUtc] datetime2(7) NULL,
        [ReasonCode] varchar(100) NULL,
        [RunId] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.DeviceEventDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.DeviceEventDaily]
    (
        [DeviceEventDailyId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [MetricKey] int NULL,
        [SourceKind] varchar(64) NULL,
        [EventCount] bigint NULL,
        [ParsedWithWarningsCount] bigint NULL,
        [OccurredTimeBasisCount] bigint NULL,
        [ReceivedTimeBasisCount] bigint NULL,
        [FirstEventAtUtc] datetime2(7) NULL,
        [LastEventAtUtc] datetime2(7) NULL,
        [LastSourcePersistedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.DeviceDailySnapshot]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.DeviceDailySnapshot]
    (
        [DeviceDailySnapshotId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [TimeZoneId] nvarchar(100) NULL,
        [BucketStartAtUtc] datetime2(7) NULL,
        [BucketEndAtUtc] datetime2(7) NULL,
        [OpeningConnectionStatus] varchar(32) NULL,
        [ClosingConnectionStatus] varchar(32) NULL,
        [ConnectedEventCount] bigint NULL,
        [DisconnectedEventCount] bigint NULL,
        [ReconnectCount] bigint NULL,
        [TotalEventCount] bigint NULL,
        [ErrorEventCount] bigint NULL,
        [WarningEventCount] bigint NULL,
        [FirstEventAtUtc] datetime2(7) NULL,
        [LastEventAtUtc] datetime2(7) NULL,
        [HealthStatus] varchar(32) NULL,
        [HealthScore] decimal(5, 2) NULL,
        [HealthRuleVersion] int NULL,
        [HealthReasonJson] nvarchar(max) NULL,
        [IsFinalized] bit NULL,
        [CalculatedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.DeviceStateDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.DeviceStateDaily]
    (
        [DeviceStateDailyId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [StateType] varchar(64) NULL,
        [BucketStartAtUtc] datetime2(7) NULL,
        [BucketEndAtUtc] datetime2(7) NULL,
        [CalculatedThroughAtUtc] datetime2(7) NULL,
        [OpeningConnectionStatus] varchar(32) NULL,
        [ClosingConnectionStatus] varchar(32) NULL,
        [OnlineSeconds] bigint NULL,
        [OfflineSeconds] bigint NULL,
        [UnknownSeconds] bigint NULL,
        [ConnectedEventCount] bigint NULL,
        [DisconnectedEventCount] bigint NULL,
        [ReconnectCount] bigint NULL,
        [OpeningEvidenceKind] varchar(64) NULL,
        [OpeningEvidenceEventId] binary(32) NULL,
        [IsDirty] bit NULL,
        [IsFinalized] bit NULL,
        [CoverageStatus] varchar(32) NULL,
        [CalculatedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.DeviceStateCursor]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.DeviceStateCursor]
    (
        [DeviceStateCursorId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StateType] varchar(64) NULL,
        [CurrentState] varchar(64) NULL,
        [StateSinceAtUtc] datetime2(7) NULL,
        [AccountedThroughAtUtc] datetime2(7) NULL,
        [LastTimelineAtUtc] datetime2(7) NULL,
        [LastEventId] binary(32) NULL,
        [OpeningEvidenceKind] varchar(64) NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProcessedEvent]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProcessedEvent]
    (
        [ProcessedEventId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [EventId] binary(32) NULL,
        [SourceDocumentId] varchar(256) NULL,
        [SourceKind] varchar(64) NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [TimelineAtUtc] datetime2(7) NULL,
        [StatisticsDate] date NULL,
        [MappingVersion] varchar(64) NULL,
        [Outcome] varchar(32) NULL,
        [ProcessedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionCheckpoint]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionCheckpoint]
    (
        [ProjectionCheckpointId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [PartitionKey] varchar(100) NULL,
        [LastPersistedAtUtc] datetime2(7) NULL,
        [LastEventId] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        [LastProcessedAtUtc] datetime2(7) NULL,
        [LastBatchSize] int NULL,
        [SweepFromAtUtc] datetime2(7) NULL,
        [SweepToAtUtc] datetime2(7) NULL,
        [SweepLastPersistedAtUtc] datetime2(7) NULL,
        [SweepLastEventId] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        [LeaseOwner] varchar(200) NULL,
        [LeaseExpiresAtUtc] datetime2(7) NULL,
        [LeaseEpoch] bigint NULL,
        [DataRevision] bigint NULL,
        [LastCompletedSweepAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.IngestionQualityDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.IngestionQualityDaily]
    (
        [IngestionQualityDailyId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionVersion] int NULL,
        [StatisticsDate] date NULL,
        [CompanyId] bigint NULL,
        [SourceKind] varchar(64) NULL,
        [SourceId] varchar(200) NULL,
        [QualityCode] varchar(100) NULL,
        [EventCount] bigint NULL,
        [FirstSeenAtUtc] datetime2(7) NULL,
        [LastSeenAtUtc] datetime2(7) NULL,
        [UpdatedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ReconciliationRequest]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ReconciliationRequest]
    (
        [ReconciliationRequestId] INT IDENTITY(1, 1) PRIMARY KEY,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StateType] varchar(64) NULL,
        [FromStatisticsDate] date NULL,
        [ToStatisticsDate] date NULL,
        [ReasonCode] varchar(64) NULL,
        [Status] varchar(32) NULL,
        [AttemptCount] int NULL,
        [NextAttemptAtUtc] datetime2(7) NULL,
        [ClaimOwner] varchar(200) NULL,
        [ClaimEpoch] bigint NULL,
        [ClaimExpiresAtUtc] datetime2(7) NULL,
        [DirtyGeneration] bigint NULL,
        [RequestedAtUtc] datetime2(7) NULL,
        [StartedAtUtc] datetime2(7) NULL,
        [CompletedAtUtc] datetime2(7) NULL,
        [ErrorSummary] nvarchar(1000) NULL,
        [EvidenceEventId] binary(32) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionFailure]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionFailure]
    (
        [ProjectionFailureId] INT IDENTITY(1, 1) PRIMARY KEY,
        [FailureId] binary(32) NULL,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [EventId] binary(32) NULL,
        [SourceEventIdentity] varchar(256) NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [SourceKind] varchar(64) NULL,
        [Category] varchar(64) NULL,
        [SourceEventName] varchar(128) NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [ErrorCode] varchar(100) NULL,
        [ErrorStage] varchar(64) NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [Retryable] bit NULL,
        [RetryCount] int NULL,
        [FirstFailedAtUtc] datetime2(7) NULL,
        [LastFailedAtUtc] datetime2(7) NULL,
        [ResolvedAtUtc] datetime2(7) NULL,
        [Resolution] nvarchar(500) NULL,
        [Version] rowversion NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionRun]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionRun]
    (
        [ProjectionRunId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [RunType] varchar(32) NULL,
        [RequestedFromDate] date NULL,
        [RequestedToDate] date NULL,
        [RequestedCompanyId] bigint NULL,
        [StartedAtUtc] datetime2(7) NULL,
        [CompletedAtUtc] datetime2(7) NULL,
        [Status] varchar(32) NULL,
        [ReadEventCount] bigint NULL,
        [AggregatedEventCount] bigint NULL,
        [DuplicateEventCount] bigint NULL,
        [IgnoredEventCount] bigint NULL,
        [FailureEventCount] bigint NULL,
        [AffectedRowCount] bigint NULL,
        [CapturedDataRevision] bigint NULL,
        [ErrorSummary] nvarchar(2000) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionStagingEvent]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionStagingEvent]
    (
        [ProjectionStagingEventId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [EventId] binary(32) NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [Outcome] varchar(32) NULL,
        [CreatedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionStagingDaily]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionStagingDaily]
    (
        [ProjectionStagingDailyId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [MetricKey] int NULL,
        [SourceKind] varchar(64) NULL,
        [EventCount] bigint NULL,
        [ParsedWithWarningsCount] bigint NULL,
        [OccurredTimeBasisCount] bigint NULL,
        [ReceivedTimeBasisCount] bigint NULL,
        [FirstEventAtUtc] datetime2(7) NULL,
        [LastEventAtUtc] datetime2(7) NULL,
        [LastSourcePersistedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionStagingState]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionStagingState]
    (
        [ProjectionStagingStateId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [StateType] varchar(64) NULL,
        [BucketStartAtUtc] datetime2(7) NULL,
        [BucketEndAtUtc] datetime2(7) NULL,
        [CalculatedThroughAtUtc] datetime2(7) NULL,
        [TimeZoneId] nvarchar(100) NULL,
        [OpeningState] varchar(64) NULL,
        [ClosingState] varchar(64) NULL,
        [OnlineSeconds] bigint NULL,
        [OfflineSeconds] bigint NULL,
        [UnknownSeconds] bigint NULL,
        [ConnectedEventCount] bigint NULL,
        [DisconnectedEventCount] bigint NULL,
        [ReconnectCount] bigint NULL,
        [OpeningEvidenceKind] varchar(64) NULL,
        [OpeningEvidenceEventId] binary(32) NULL,
        [IsDirty] bit NULL,
        [IsFinalized] bit NULL,
        [CoverageStatus] varchar(32) NULL,
        [CreatedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionStagingSummary]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionStagingSummary]
    (
        [ProjectionStagingSummaryId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [EventCount] bigint NULL,
        [ErrorEventCount] bigint NULL,
        [WarningEventCount] bigint NULL,
        [FirstEventAtUtc] datetime2(7) NULL,
        [LastEventAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionStagingCoverage]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionStagingCoverage]
    (
        [ProjectionStagingCoverageId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [ProjectionName] varchar(100) NULL,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [CoverageKind] varchar(64) NULL,
        [CoverageStatus] varchar(32) NULL,
        [CoveredFromAtUtc] datetime2(7) NULL,
        [CoveredThroughAtUtc] datetime2(7) NULL,
        [ReasonCode] varchar(100) NULL,
        [CreatedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionStagingQuality]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionStagingQuality]
    (
        [ProjectionStagingQualityId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [ProjectionVersion] int NULL,
        [StatisticsDate] date NULL,
        [CompanyId] bigint NULL,
        [SourceKind] varchar(64) NULL,
        [SourceId] varchar(200) NULL,
        [QualityCode] varchar(100) NULL,
        [EventCount] bigint NULL,
        [FirstSeenAtUtc] datetime2(7) NULL,
        [LastSeenAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[DES.ProjectionStagingCursor]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DES.ProjectionStagingCursor]
    (
        [ProjectionStagingCursorId] INT IDENTITY(1, 1) PRIMARY KEY,
        [RunId] uniqueidentifier NULL,
        [ProjectionVersion] int NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StateType] varchar(64) NULL,
        [CurrentState] varchar(64) NULL,
        [StateSinceAtUtc] datetime2(7) NULL,
        [AccountedThroughAtUtc] datetime2(7) NULL,
        [LastTimelineAtUtc] datetime2(7) NULL,
        [LastEventId] binary(32) NULL,
        [OpeningEvidenceKind] varchar(64) NULL,
        [CreatedAtUtc] datetime2(7) NULL
    );
END;

IF TYPE_ID(N'[dbo].[ProjectionProcessedEventType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionProcessedEventType] AS TABLE
    (
        [EventId] binary(32) NULL,
        [SourceDocumentId] varchar(256) NULL,
        [SourceKind] varchar(64) NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [StatisticsDate] date NULL,
        [TimelineAtUtc] datetime2(7) NULL,
        [MappingVersion] varchar(64) NULL,
        [Outcome] varchar(32) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionMetricContributionType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionMetricContributionType] AS TABLE
    (
        [EventId] binary(32) NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [MetricKey] int NULL,
        [SourceKind] varchar(64) NULL,
        [TimelineAtUtc] datetime2(7) NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [ParsedWithWarnings] bit NULL,
        [TimeBasis] varchar(16) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionDeviceSummaryType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionDeviceSummaryType] AS TABLE
    (
        [EventId] binary(32) NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [SourceKind] varchar(64) NULL,
        [IsError] bit NULL,
        [IsWarning] bit NULL,
        [TimelineAtUtc] datetime2(7) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionStateObservationType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionStateObservationType] AS TABLE
    (
        [EventId] binary(32) NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [StateType] varchar(64) NULL,
        [ObservedState] varchar(64) NULL,
        [TimelineAtUtc] datetime2(7) NULL,
        [OpeningEvidenceKind] varchar(64) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionQualityContributionType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionQualityContributionType] AS TABLE
    (
        [EventId] binary(32) NULL,
        [QualityIdentity] binary(32) NULL,
        [StatisticsDate] date NULL,
        [CompanyId] bigint NULL,
        [SourceKind] varchar(64) NULL,
        [SourceId] varchar(200) NULL,
        [QualityCode] varchar(100) NULL,
        [SeenAtUtc] datetime2(7) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionFailureType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionFailureType] AS TABLE
    (
        [FailureId] binary(32) NULL,
        [EventId] binary(32) NULL,
        [SourceEventIdentity] varchar(256) NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [SourceKind] varchar(64) NULL,
        [Category] varchar(64) NULL,
        [SourceEventName] varchar(128) NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [ErrorCode] varchar(100) NULL,
        [ErrorStage] varchar(64) NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [Retryable] bit NULL,
        [RetryCount] int NULL,
        [FirstFailedAtUtc] datetime2(7) NULL,
        [LastFailedAtUtc] datetime2(7) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionStateDailyType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionStateDailyType] AS TABLE
    (
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StatisticsDate] date NULL,
        [StateType] varchar(64) NULL,
        [BucketStartAtUtc] datetime2(7) NULL,
        [BucketEndAtUtc] datetime2(7) NULL,
        [CalculatedThroughAtUtc] datetime2(7) NULL,
        [TimeZoneId] nvarchar(100) NULL,
        [OpeningState] varchar(64) NULL,
        [ClosingState] varchar(64) NULL,
        [OnlineSeconds] bigint NULL,
        [OfflineSeconds] bigint NULL,
        [UnknownSeconds] bigint NULL,
        [ConnectedEventCount] bigint NULL,
        [DisconnectedEventCount] bigint NULL,
        [ReconnectCount] bigint NULL,
        [OpeningEvidenceKind] varchar(64) NULL,
        [OpeningEvidenceEventId] binary(32) NULL,
        [IsDirty] bit NULL,
        [IsFinalized] bit NULL,
        [CoverageStatus] varchar(32) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionStateCursorType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionStateCursorType] AS TABLE
    (
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StateType] varchar(64) NULL,
        [CurrentState] varchar(64) NULL,
        [StateSinceAtUtc] datetime2(7) NULL,
        [AccountedThroughAtUtc] datetime2(7) NULL,
        [LastTimelineAtUtc] datetime2(7) NULL,
        [LastEventId] binary(32) NULL,
        [OpeningEvidenceKind] varchar(64) NULL
    )');

IF TYPE_ID(N'[dbo].[ProjectionReconciliationRequestType]') IS NULL
    EXEC(N'CREATE TYPE [dbo].[ProjectionReconciliationRequestType] AS TABLE
    (
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [StateType] varchar(64) NULL,
        [FromStatisticsDate] date NULL,
        [ToStatisticsDate] date NULL,
        [ReasonCode] varchar(64) NULL,
        [RequestedAtUtc] datetime2(7) NULL,
        [EvidenceEventId] binary(32) NULL
    )');

CREATE INDEX [IX_DES_ProjectionDefinition_Identity]
    ON [dbo].[DES.ProjectionDefinition] ([ProjectionName], [ProjectionVersion]);

CREATE INDEX [IX_DES_DeviceDimension_Identity]
    ON [dbo].[DES.DeviceDimension] ([CompanyId], [DeviceId]);

CREATE INDEX [IX_DES_MetricDefinition_Identity]
    ON [dbo].[DES.MetricDefinition] ([MetricSetVersion], [MetricCode]);

CREATE INDEX [IX_DES_ProjectionCoverage_Identity]
    ON [dbo].[DES.ProjectionCoverage]
        ([ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [CoverageKind]);

CREATE INDEX [IX_DES_DeviceEventDaily_Identity]
    ON [dbo].[DES.DeviceEventDaily]
        ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind]);

CREATE INDEX [IX_DES_DeviceDailySnapshot_Identity]
    ON [dbo].[DES.DeviceDailySnapshot] ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate]);

CREATE INDEX [IX_DES_DeviceStateDaily_Identity]
    ON [dbo].[DES.DeviceStateDaily]
        ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [StateType]);

CREATE INDEX [IX_DES_DeviceStateCursor_Identity]
    ON [dbo].[DES.DeviceStateCursor] ([ProjectionVersion], [CompanyId], [DeviceId], [StateType]);

CREATE INDEX [IX_DES_ProcessedEvent_Identity]
    ON [dbo].[DES.ProcessedEvent] ([ProjectionName], [ProjectionVersion], [EventId]);

CREATE INDEX [IX_DES_ProjectionCheckpoint_Identity]
    ON [dbo].[DES.ProjectionCheckpoint] ([ProjectionName], [ProjectionVersion], [PartitionKey]);

CREATE INDEX [IX_DES_IngestionQualityDaily_Identity]
    ON [dbo].[DES.IngestionQualityDaily]
        ([ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode]);

CREATE INDEX [IX_DES_ReconciliationRequest_Status]
    ON [dbo].[DES.ReconciliationRequest]
        ([ProjectionName], [ProjectionVersion], [Status], [RequestedAtUtc]);

CREATE INDEX [IX_DES_ProjectionFailure_Identity]
    ON [dbo].[DES.ProjectionFailure] ([ProjectionName], [ProjectionVersion], [FailureId]);

CREATE INDEX [IX_DES_ProjectionRun_RunId]
    ON [dbo].[DES.ProjectionRun] ([RunId]);

CREATE INDEX [IX_DES_ProjectionStagingEvent_Run]
    ON [dbo].[DES.ProjectionStagingEvent] ([RunId], [EventId]);

CREATE INDEX [IX_DES_ProjectionStagingDaily_Run]
    ON [dbo].[DES.ProjectionStagingDaily]
        ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind]);

CREATE INDEX [IX_DES_ProjectionStagingState_Run]
    ON [dbo].[DES.ProjectionStagingState]
        ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [StateType]);

CREATE INDEX [IX_DES_ProjectionStagingSummary_Run]
    ON [dbo].[DES.ProjectionStagingSummary]
        ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate]);

CREATE INDEX [IX_DES_ProjectionStagingCoverage_Run]
    ON [dbo].[DES.ProjectionStagingCoverage]
        ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [CoverageKind]);

CREATE INDEX [IX_DES_ProjectionStagingQuality_Run]
    ON [dbo].[DES.ProjectionStagingQuality]
        ([RunId], [ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode]);

CREATE INDEX [IX_DES_ProjectionStagingCursor_Run]
    ON [dbo].[DES.ProjectionStagingCursor] ([RunId], [ProjectionVersion], [CompanyId], [DeviceId], [StateType]);

DECLARE @now datetime2(7) = SYSUTCDATETIME();

INSERT INTO [dbo].[DES.MetricDefinition]
(
    [MetricKey], [MetricSetVersion], [MetricCode], [DisplayName], [MetricGroup], [Unit],
    [DefaultCategory], [PrimarySourceKind], [IsHealthInput], [IsEnabled],
    [MappingVersion], [OwnershipVersion], [CreatedAtUtc], [UpdatedAtUtc]
)
SELECT
    seed.[MetricKey], seed.[MetricSetVersion], seed.[MetricCode], seed.[DisplayName], seed.[MetricGroup], seed.[Unit],
    seed.[DefaultCategory], seed.[PrimarySourceKind], seed.[IsHealthInput], seed.[IsEnabled],
    seed.[MappingVersion], seed.[OwnershipVersion], @now, @now
FROM
(
    VALUES
        (1, 1, 'tag_read', 'Tag read', 'activity', 'count', 'tag', 'rfid_antenna_file', 0, 0, 'v1', 'v1'),
        (2, 1, 'business_process', 'Business process', 'business', 'count', 'business', 'rfid_antenna_file', 0, 0, 'v1', 'v1'),
        (3, 1, 'device_online_observed', 'Device online observed', 'connection', 'count', 'connection', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (4, 1, 'device_connected', 'Device connected', 'connection', 'count', 'connection', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (5, 1, 'device_disconnected', 'Device disconnected', 'connection', 'count', 'connection', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (6, 1, 'scanner_connected', 'Scanner connected', 'scanner', 'count', 'scanner', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (7, 1, 'scanner_disconnected', 'Scanner disconnected', 'scanner', 'count', 'scanner', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (8, 1, 'green_light_on', 'Green light on', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (9, 1, 'green_light_off', 'Green light off', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (10, 1, 'red_light_on', 'Red light on', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (11, 1, 'red_light_off', 'Red light off', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (12, 1, 'sensor_state_observed', 'Sensor state observed', 'sensor', 'count', 'sensor', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (13, 1, 'device_error', 'Device error', 'error', 'count', 'error', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (14, 1, 'snapshot_observed', 'Snapshot observed', 'connection', 'count', 'snapshot', 'erp_apphub', 0, 0, 'v1', 'v1')
) AS seed
(
    [MetricKey], [MetricSetVersion], [MetricCode], [DisplayName], [MetricGroup], [Unit],
    [DefaultCategory], [PrimarySourceKind], [IsHealthInput], [IsEnabled],
    [MappingVersion], [OwnershipVersion]
)
WHERE NOT EXISTS
(
    SELECT 1
    FROM [dbo].[DES.MetricDefinition] existing
    WHERE existing.[MetricSetVersion] = seed.[MetricSetVersion]
      AND existing.[MetricCode] = seed.[MetricCode]
);

IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[DES.SchemaMigration]
    WHERE [MigrationId] = '009_CreateDeviceEventStatisticsSchema'
)
BEGIN
    INSERT INTO [dbo].[DES.SchemaMigration] ([MigrationId], [Checksum], [AppliedAtUtc], [AppliedBy])
    VALUES
    (
        '009_CreateDeviceEventStatisticsSchema',
        HASHBYTES('SHA2_256', CONVERT(varbinary(max), N'009_CreateDeviceEventStatisticsSchema')),
        SYSUTCDATETIME(),
        SUSER_SNAME()
    );
END;
