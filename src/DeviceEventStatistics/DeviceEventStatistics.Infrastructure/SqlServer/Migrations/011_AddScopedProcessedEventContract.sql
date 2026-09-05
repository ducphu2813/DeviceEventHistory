IF COL_LENGTH(N'[__SCHEMA__].[DES.ProcessedEvent]', N'CompanyId') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[DES.ProcessedEvent]
        ADD [CompanyId] bigint NULL;
END;

IF COL_LENGTH(N'[__SCHEMA__].[DES.ProcessedEvent]', N'DeviceId') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[DES.ProcessedEvent]
        ADD [DeviceId] bigint NULL;
END;

IF TYPE_ID(N'[__SCHEMA__].[ProjectionProcessedEventTypeV2]') IS NULL
    EXEC(N'CREATE TYPE [__SCHEMA__].[ProjectionProcessedEventTypeV2] AS TABLE
    (
        [EventId] binary(32) NULL,
        [SourceDocumentId] varchar(256) NULL,
        [SourceKind] varchar(64) NULL,
        [CompanyId] bigint NULL,
        [DeviceId] bigint NULL,
        [SourcePersistedAtUtc] datetime2(7) NULL,
        [StatisticsDate] date NULL,
        [TimelineAtUtc] datetime2(7) NULL,
        [MappingVersion] varchar(64) NULL,
        [Outcome] varchar(32) NULL
    )');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[DES.ProcessedEvent]')
      AND [name] = N'IX_DES_ProcessedEvent_Scope'
)
BEGIN
    CREATE INDEX [IX_DES_ProcessedEvent_Scope]
        ON [__SCHEMA__].[DES.ProcessedEvent]
            ([ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [TimelineAtUtc]);
END;
