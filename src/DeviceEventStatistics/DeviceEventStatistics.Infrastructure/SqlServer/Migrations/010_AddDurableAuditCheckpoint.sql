IF COL_LENGTH(N'[__SCHEMA__].[DES.ProjectionCheckpoint]', N'LastCompletedSweepAtUtc') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[DES.ProjectionCheckpoint]
        ADD [LastCompletedSweepAtUtc] datetime2(7) NULL;
END;

IF COL_LENGTH(N'[__SCHEMA__].[DES.ProjectionCheckpoint]', N'AuditLastSourceDocumentId') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[DES.ProjectionCheckpoint]
        ADD [AuditLastSourceDocumentId] varchar(256) NULL;
END;

IF COL_LENGTH(N'[__SCHEMA__].[DES.ProjectionCheckpoint]', N'AuditStartedAtUtc') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[DES.ProjectionCheckpoint]
        ADD [AuditStartedAtUtc] datetime2(7) NULL;
END;

IF COL_LENGTH(N'[__SCHEMA__].[DES.ProjectionCheckpoint]', N'AuditCompletedAtUtc') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[DES.ProjectionCheckpoint]
        ADD [AuditCompletedAtUtc] datetime2(7) NULL;
END;

IF COL_LENGTH(N'[__SCHEMA__].[DES.ProjectionCheckpoint]', N'AuditCycle') IS NULL
BEGIN
    ALTER TABLE [__SCHEMA__].[DES.ProjectionCheckpoint]
        ADD [AuditCycle] bigint NOT NULL
            CONSTRAINT [DF_DES_ProjectionCheckpoint_AuditCycle] DEFAULT 0;
END;
