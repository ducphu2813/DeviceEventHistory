-- Retention cleanup support for the standalone DES schema.
-- This migration is non-destructive: it only adds the index needed to remove
-- aged ProcessedEvent ledger rows in ordered, bounded batches.

SET NOCOUNT ON;

IF OBJECT_ID(N'[dbo].[DES.ProcessedEvent]', N'U') IS NULL
BEGIN
    THROW 51000, 'DES.ProcessedEvent must exist before applying 011_AddRetentionCleanupIndexes.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[DES.ProcessedEvent]')
      AND [name] = N'IX_DES_ProcessedEvent_Retention'
)
BEGIN
    CREATE INDEX [IX_DES_ProcessedEvent_Retention]
        ON [dbo].[DES.ProcessedEvent]
        ([ProjectionName], [ProjectionVersion], [SourcePersistedAtUtc], [ProcessedEventId]);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[DES.SchemaMigration]
    WHERE [MigrationId] = '011_AddRetentionCleanupIndexes'
)
BEGIN
    INSERT INTO [dbo].[DES.SchemaMigration]
        ([MigrationId], [Checksum], [AppliedAtUtc], [AppliedBy])
    VALUES
    (
        '011_AddRetentionCleanupIndexes',
        HASHBYTES('SHA2_256', CONVERT(varbinary(max), N'011_AddRetentionCleanupIndexes')),
        SYSUTCDATETIME(),
        SUSER_SNAME()
    );
END;
