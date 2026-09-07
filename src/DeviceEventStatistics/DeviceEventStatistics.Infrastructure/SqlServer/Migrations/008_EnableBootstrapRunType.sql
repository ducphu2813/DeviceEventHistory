IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[__SCHEMA__].[ProjectionRun]')
      AND [name] = N'CK_ProjectionRun_Type'
)
BEGIN
    ALTER TABLE [__SCHEMA__].[ProjectionRun]
        DROP CONSTRAINT [CK_ProjectionRun_Type];
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[__SCHEMA__].[ProjectionRun]')
      AND [name] = N'CK_ProjectionRun_Type'
)
BEGIN
    ALTER TABLE [__SCHEMA__].[ProjectionRun]
        ADD CONSTRAINT [CK_ProjectionRun_Type]
        CHECK ([RunType] IN ('incremental', 'reconciliation', 'bootstrap', 'backfill', 'rebuild'));
END;
