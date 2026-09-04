DECLARE @now datetime2(7) = SYSUTCDATETIME();

INSERT INTO [__SCHEMA__].[MetricDefinition]
(
    [MetricSetVersion], [MetricCode], [DisplayName], [MetricGroup], [Unit],
    [DefaultCategory], [PrimarySourceKind], [IsHealthInput], [IsEnabled],
    [MappingVersion], [OwnershipVersion], [CreatedAtUtc], [UpdatedAtUtc]
)
SELECT
    seed.[MetricSetVersion], seed.[MetricCode], seed.[DisplayName], seed.[MetricGroup], seed.[Unit],
    seed.[DefaultCategory], seed.[PrimarySourceKind], seed.[IsHealthInput], seed.[IsEnabled],
    seed.[MappingVersion], seed.[OwnershipVersion], @now, @now
FROM
(
    VALUES
        (1, 'tag_read', 'Tag read', 'activity', 'count', 'tag', 'rfid_antenna_file', 0, 0, 'v1', 'v1'),
        (1, 'business_process', 'Business process', 'business', 'count', 'business', 'rfid_antenna_file', 0, 0, 'v1', 'v1'),
        (1, 'device_online_observed', 'Device online observed', 'connection', 'count', 'connection', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (1, 'device_connected', 'Device connected', 'connection', 'count', 'connection', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (1, 'device_disconnected', 'Device disconnected', 'connection', 'count', 'connection', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (1, 'scanner_connected', 'Scanner connected', 'scanner', 'count', 'scanner', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (1, 'scanner_disconnected', 'Scanner disconnected', 'scanner', 'count', 'scanner', 'erp_apphub', 1, 0, 'v1', 'v1'),
        (1, 'green_light_on', 'Green light on', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (1, 'green_light_off', 'Green light off', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (1, 'red_light_on', 'Red light on', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (1, 'red_light_off', 'Red light off', 'control', 'count', 'control', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (1, 'sensor_state_observed', 'Sensor state observed', 'sensor', 'count', 'sensor', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (1, 'device_error', 'Device error', 'error', 'count', 'error', 'erp_apphub', 0, 0, 'v1', 'v1'),
        (1, 'snapshot_observed', 'Snapshot observed', 'connection', 'count', 'snapshot', 'erp_apphub', 0, 0, 'v1', 'v1')
) AS seed
(
    [MetricSetVersion], [MetricCode], [DisplayName], [MetricGroup], [Unit],
    [DefaultCategory], [PrimarySourceKind], [IsHealthInput], [IsEnabled],
    [MappingVersion], [OwnershipVersion]
)
WHERE NOT EXISTS
(
    SELECT 1
    FROM [__SCHEMA__].[MetricDefinition] existing
    WHERE existing.[MetricSetVersion] = seed.[MetricSetVersion]
      AND existing.[MetricCode] = seed.[MetricCode]
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[DeviceEventDaily]') AND [name] = N'IX_DeviceEventDaily_Company_Date_Metric')
BEGIN
    CREATE INDEX [IX_DeviceEventDaily_Company_Date_Metric]
        ON [__SCHEMA__].[DeviceEventDaily] ([ProjectionVersion], [CompanyId], [StatisticsDate], [MetricKey])
        INCLUDE ([DeviceId], [SourceKind], [EventCount], [FirstEventAtUtc], [LastEventAtUtc]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[DeviceEventDaily]') AND [name] = N'IX_DeviceEventDaily_Company_Metric_Date')
BEGIN
    CREATE INDEX [IX_DeviceEventDaily_Company_Metric_Date]
        ON [__SCHEMA__].[DeviceEventDaily] ([ProjectionVersion], [CompanyId], [MetricKey], [StatisticsDate])
        INCLUDE ([DeviceId], [SourceKind], [EventCount]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[DeviceDailySnapshot]') AND [name] = N'IX_DeviceDailySnapshot_Company_Date_Health')
BEGIN
    CREATE INDEX [IX_DeviceDailySnapshot_Company_Date_Health]
        ON [__SCHEMA__].[DeviceDailySnapshot] ([ProjectionVersion], [CompanyId], [StatisticsDate], [HealthStatus])
        INCLUDE ([DeviceId], [HealthScore], [FirstEventAtUtc], [LastEventAtUtc]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[ProjectionCheckpoint]') AND [name] = N'IX_ProjectionCheckpoint_Lease')
BEGIN
    CREATE INDEX [IX_ProjectionCheckpoint_Lease]
        ON [__SCHEMA__].[ProjectionCheckpoint] ([ProjectionName], [ProjectionVersion], [LeaseExpiresAtUtc])
        INCLUDE ([PartitionKey], [LeaseOwner], [LeaseEpoch], [DataRevision]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[ReconciliationRequest]') AND [name] = N'IX_ReconciliationRequest_Status_Requested')
BEGIN
    CREATE INDEX [IX_ReconciliationRequest_Status_Requested]
        ON [__SCHEMA__].[ReconciliationRequest] ([ProjectionName], [ProjectionVersion], [Status], [RequestedAtUtc])
        INCLUDE ([CompanyId], [DeviceId], [StateType], [FromStatisticsDate], [ToStatisticsDate], [AttemptCount], [NextAttemptAtUtc]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[ProjectionRun]') AND [name] = N'IX_ProjectionRun_Name_StartedAtUtc')
BEGIN
    CREATE INDEX [IX_ProjectionRun_Name_StartedAtUtc]
        ON [__SCHEMA__].[ProjectionRun] ([ProjectionName], [ProjectionVersion], [StartedAtUtc] DESC);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[__SCHEMA__].[ProjectionStagingDaily]') AND [name] = N'IX_ProjectionStagingDaily_Run')
BEGIN
    CREATE INDEX [IX_ProjectionStagingDaily_Run]
        ON [__SCHEMA__].[ProjectionStagingDaily] ([RunId], [StatisticsDate], [CompanyId], [DeviceId]);
END;
