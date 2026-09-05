DECLARE @now datetime2(7) = SYSUTCDATETIME();

UPDATE metric
SET
    [IsEnabled] = seed.[IsEnabled],
    [MappingVersion] = seed.[MappingVersion],
    [OwnershipVersion] = seed.[OwnershipVersion],
    [UpdatedAtUtc] = @now
FROM [__SCHEMA__].[DES.MetricDefinition] metric
JOIN
(
    VALUES
        (1, 1, 'tag_read', 1, 'v1', 'v1'),
        (2, 1, 'business_process', 1, 'v1', 'v1'),
        (3, 1, 'device_online_observed', 1, 'v1', 'v1'),
        (4, 1, 'device_connected', 1, 'v1', 'v1'),
        (5, 1, 'device_disconnected', 1, 'v1', 'v1'),
        (6, 1, 'scanner_connected', 1, 'v1', 'v1'),
        (7, 1, 'scanner_disconnected', 1, 'v1', 'v1'),
        (8, 1, 'green_light_on', 1, 'v1', 'v1'),
        (9, 1, 'green_light_off', 1, 'v1', 'v1'),
        (10, 1, 'red_light_on', 1, 'v1', 'v1'),
        (11, 1, 'red_light_off', 1, 'v1', 'v1'),
        (12, 1, 'sensor_state_observed', 1, 'v1', 'v1'),
        (13, 1, 'device_error', 0, 'v1', 'v1'),
        (14, 1, 'snapshot_observed', 1, 'v1', 'v1')
) seed ([MetricKey], [MetricSetVersion], [MetricCode], [IsEnabled], [MappingVersion], [OwnershipVersion])
    ON metric.[MetricKey] = seed.[MetricKey]
   AND metric.[MetricSetVersion] = seed.[MetricSetVersion]
   AND metric.[MetricCode] = seed.[MetricCode]
WHERE metric.[MetricKey] = seed.[MetricKey]
  AND metric.[MetricSetVersion] = seed.[MetricSetVersion]
  AND metric.[MetricCode] = seed.[MetricCode];
