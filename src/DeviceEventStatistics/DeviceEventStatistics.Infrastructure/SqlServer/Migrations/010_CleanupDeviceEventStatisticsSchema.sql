-- Device Event Statistics - destructive cleanup for a fresh 009 bootstrap.
-- Execute this file in the database selected in SSMS or Azure Data Studio.
-- The script is intentionally limited to legacy Statistics objects in dbo.
-- It does not select a database and does not touch History or ERP tables.

SET NOCOUNT ON;

DROP TABLE IF EXISTS [dbo].[DES.ProjectionStagingCursor];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionStagingQuality];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionStagingCoverage];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionStagingState];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionStagingDaily];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionStagingEvent];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionStagingSummary];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionRun];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionFailure];
DROP TABLE IF EXISTS [dbo].[DES.ReconciliationRequest];
DROP TABLE IF EXISTS [dbo].[DES.IngestionQualityDaily];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionCheckpoint];
DROP TABLE IF EXISTS [dbo].[DES.ProcessedEvent];
DROP TABLE IF EXISTS [dbo].[DES.DeviceStateCursor];
DROP TABLE IF EXISTS [dbo].[DES.DeviceStateDaily];
DROP TABLE IF EXISTS [dbo].[DES.DeviceDailySnapshot];
DROP TABLE IF EXISTS [dbo].[DES.DeviceEventDaily];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionCoverage];
DROP TABLE IF EXISTS [dbo].[DES.MetricDefinition];
DROP TABLE IF EXISTS [dbo].[DES.DeviceDimension];
DROP TABLE IF EXISTS [dbo].[DES.ProjectionDefinition];
DROP TABLE IF EXISTS [dbo].[DES.SchemaMigration];

DROP TABLE IF EXISTS [dbo].[ProjectionStagingCursor];
DROP TABLE IF EXISTS [dbo].[ProjectionStagingQuality];
DROP TABLE IF EXISTS [dbo].[ProjectionStagingCoverage];
DROP TABLE IF EXISTS [dbo].[ProjectionStagingState];
DROP TABLE IF EXISTS [dbo].[ProjectionStagingDaily];
DROP TABLE IF EXISTS [dbo].[ProjectionStagingEvent];
DROP TABLE IF EXISTS [dbo].[ProjectionStagingSummary];
DROP TABLE IF EXISTS [dbo].[ProjectionRun];
DROP TABLE IF EXISTS [dbo].[ProjectionFailure];
DROP TABLE IF EXISTS [dbo].[ReconciliationRequest];
DROP TABLE IF EXISTS [dbo].[IngestionQualityDaily];
DROP TABLE IF EXISTS [dbo].[ProjectionCheckpoint];
DROP TABLE IF EXISTS [dbo].[ProcessedEvent];
DROP TABLE IF EXISTS [dbo].[DeviceStateCursor];
DROP TABLE IF EXISTS [dbo].[DeviceStateDaily];
DROP TABLE IF EXISTS [dbo].[DeviceDailySnapshot];
DROP TABLE IF EXISTS [dbo].[DeviceEventDaily];
DROP TABLE IF EXISTS [dbo].[ProjectionCoverage];
DROP TABLE IF EXISTS [dbo].[MetricDefinition];
DROP TABLE IF EXISTS [dbo].[DeviceDimension];
DROP TABLE IF EXISTS [dbo].[ProjectionDefinition];
DROP TABLE IF EXISTS [dbo].[SchemaMigration];

DROP TYPE IF EXISTS [dbo].[ProjectionProcessedEventTypeV2];
DROP TYPE IF EXISTS [dbo].[ProjectionProcessedEventType];
DROP TYPE IF EXISTS [dbo].[ProjectionMetricContributionType];
DROP TYPE IF EXISTS [dbo].[ProjectionDeviceSummaryType];
DROP TYPE IF EXISTS [dbo].[ProjectionStateObservationType];
DROP TYPE IF EXISTS [dbo].[ProjectionQualityContributionType];
DROP TYPE IF EXISTS [dbo].[ProjectionFailureType];
DROP TYPE IF EXISTS [dbo].[ProjectionStateDailyType];
DROP TYPE IF EXISTS [dbo].[ProjectionStateCursorType];
DROP TYPE IF EXISTS [dbo].[ProjectionReconciliationRequestType];
