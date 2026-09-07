IF TYPE_ID(N'[__SCHEMA__].[ProjectionStateDailyType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionStateDailyType] AS TABLE
    (
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StatisticsDate] date NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [BucketStartAtUtc] datetime2(7) NOT NULL,
        [BucketEndAtUtc] datetime2(7) NOT NULL,
        [CalculatedThroughAtUtc] datetime2(7) NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [OpeningState] varchar(64) NOT NULL,
        [ClosingState] varchar(64) NOT NULL,
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
        PRIMARY KEY ([CompanyId], [DeviceId], [StatisticsDate], [StateType])
    )');
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionStateCursorType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionStateCursorType] AS TABLE
    (
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [CurrentState] varchar(64) NOT NULL,
        [StateSinceAtUtc] datetime2(7) NOT NULL,
        [AccountedThroughAtUtc] datetime2(7) NOT NULL,
        [LastTimelineAtUtc] datetime2(7) NOT NULL,
        [LastEventId] binary(32) NOT NULL,
        [OpeningEvidenceKind] varchar(64) NOT NULL,
        PRIMARY KEY ([CompanyId], [DeviceId], [StateType])
    )');
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionReconciliationRequestType]') IS NULL
BEGIN
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionReconciliationRequestType] AS TABLE
    (
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [StateType] varchar(64) NOT NULL,
        [FromStatisticsDate] date NOT NULL,
        [ToStatisticsDate] date NOT NULL,
        [ReasonCode] varchar(64) NOT NULL,
        [RequestedAtUtc] datetime2(7) NOT NULL,
        [EvidenceEventId] binary(32) NOT NULL,
        PRIMARY KEY ([CompanyId], [DeviceId], [StateType], [ReasonCode])
    )');
END;
