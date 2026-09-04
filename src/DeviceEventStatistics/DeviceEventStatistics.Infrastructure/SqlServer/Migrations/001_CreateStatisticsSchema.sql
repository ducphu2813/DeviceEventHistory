IF SCHEMA_ID(N'__SCHEMA__') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [__SCHEMA__] AUTHORIZATION [dbo]');
END;

IF OBJECT_ID(N'[__SCHEMA__].[SchemaMigration]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[SchemaMigration]
    (
        [MigrationId] varchar(100) NOT NULL,
        [Checksum] binary(32) NOT NULL,
        [AppliedAtUtc] datetime2(7) NOT NULL,
        [AppliedBy] varchar(128) NOT NULL,
        CONSTRAINT [PK_SchemaMigration] PRIMARY KEY CLUSTERED ([MigrationId])
    );
END;
