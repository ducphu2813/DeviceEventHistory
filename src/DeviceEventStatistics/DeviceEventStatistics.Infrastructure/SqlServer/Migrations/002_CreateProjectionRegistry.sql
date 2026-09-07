IF OBJECT_ID(N'[__SCHEMA__].[ProjectionDefinition]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionDefinition]
    (
        [ProjectionName] varchar(100) NOT NULL,
        [ProjectionVersion] int NOT NULL,
        [MappingVersion] varchar(64) NOT NULL,
        [OwnershipVersion] varchar(64) NOT NULL,
        [MetricSetVersion] int NOT NULL,
        [CoverageStartAtUtc] datetime2(7) NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [LifecycleStatus] varchar(32) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [ActivatedAtUtc] datetime2(7) NULL,
        [RetiredAtUtc] datetime2(7) NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_ProjectionDefinition] PRIMARY KEY CLUSTERED ([ProjectionName], [ProjectionVersion]),
        CONSTRAINT [CK_ProjectionDefinition_Version] CHECK ([ProjectionVersion] > 0),
        CONSTRAINT [CK_ProjectionDefinition_MetricSetVersion] CHECK ([MetricSetVersion] > 0),
        CONSTRAINT [CK_ProjectionDefinition_Status] CHECK ([LifecycleStatus] IN ('building', 'ready', 'active', 'retired', 'failed'))
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[DeviceDimension]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[DeviceDimension]
    (
        [CompanyId] bigint NOT NULL,
        [DeviceId] bigint NOT NULL,
        [DeviceCode] nvarchar(100) NULL,
        [DeviceName] nvarchar(250) NULL,
        [DeviceType] varchar(64) NULL,
        [GateId] bigint NULL,
        [GateCode] nvarchar(100) NULL,
        [GateName] nvarchar(250) NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [TimeZoneEffectiveFromUtc] datetime2(7) NOT NULL CONSTRAINT [DF_DeviceDimension_TimeZoneEffectiveFromUtc] DEFAULT '2000-01-01T00:00:00',
        [IsActive] bit NULL,
        [MetadataSource] varchar(64) NOT NULL,
        [MetadataUpdatedAtUtc] datetime2(7) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_DeviceDimension] PRIMARY KEY CLUSTERED ([CompanyId], [DeviceId]),
        CONSTRAINT [CK_DeviceDimension_PositiveCompany] CHECK ([CompanyId] > 0),
        CONSTRAINT [CK_DeviceDimension_PositiveDevice] CHECK ([DeviceId] > 0)
    );
END;

IF OBJECT_ID(N'[__SCHEMA__].[MetricDefinition]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[MetricDefinition]
    (
        [MetricKey] int IDENTITY(1,1) NOT NULL,
        [MetricSetVersion] int NOT NULL,
        [MetricCode] varchar(100) NOT NULL,
        [DisplayName] nvarchar(250) NOT NULL,
        [MetricGroup] varchar(64) NOT NULL,
        [Unit] varchar(32) NOT NULL,
        [DefaultCategory] varchar(64) NULL,
        [PrimarySourceKind] varchar(64) NULL,
        [IsHealthInput] bit NOT NULL,
        [IsEnabled] bit NOT NULL,
        [MappingVersion] varchar(64) NOT NULL,
        [OwnershipVersion] varchar(64) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_MetricDefinition] PRIMARY KEY CLUSTERED ([MetricKey]),
        CONSTRAINT [CK_MetricDefinition_MetricSetVersion] CHECK ([MetricSetVersion] > 0)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[MetricDefinition]')
      AND [name] = N'UX_MetricDefinition_MetricSet_MetricCode'
)
BEGIN
    CREATE UNIQUE INDEX [UX_MetricDefinition_MetricSet_MetricCode]
        ON [__SCHEMA__].[MetricDefinition] ([MetricSetVersion], [MetricCode]);
END;

IF OBJECT_ID(N'[__SCHEMA__].[ProjectionCoverage]', N'U') IS NULL
BEGIN
    CREATE TABLE [__SCHEMA__].[ProjectionCoverage]
    (
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
        [RunId] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [Version] rowversion NOT NULL,
        CONSTRAINT [PK_ProjectionCoverage] PRIMARY KEY CLUSTERED
            ([ProjectionName], [ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [CoverageKind]),
        CONSTRAINT [CK_ProjectionCoverage_Dates] CHECK ([CoveredFromAtUtc] <= [CoveredThroughAtUtc]),
        CONSTRAINT [CK_ProjectionCoverage_Status] CHECK ([CoverageStatus] IN ('complete', 'partial', 'unrecoverable'))
    );
END;
