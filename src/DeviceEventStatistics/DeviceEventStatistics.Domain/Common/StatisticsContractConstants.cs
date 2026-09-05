using System.Globalization;

namespace DeviceEventStatistics.Domain.Common;

public static class StatisticsContractConstants
{
    public const string MessageCodePrefix = "STAT-";
    public const string ProjectionName = "device_event_daily";
    public const string DefaultPartitionKey = "device_event_history";
    public const string DefaultTimeZoneId = "Asia/Ho_Chi_Minh";

    public static class Outcomes
    {
        public const string Aggregated = "aggregated";
        public const string Ignored = "ignored";
        public const string QualityOnly = "quality_only";
        public const string FailedTerminal = "failed_terminal";
    }

    public static class LeaseErrors
    {
        public const string NotOwned = "STAT-LEASE-NOT-OWNED";
        public const string Lost = "STAT-LEASE-LOST";
    }

    public static class StartupErrors
    {
        public const string Cancelled = "STAT-STARTUP-CANCELLED";
        public const string Timeout = "STAT-STARTUP-TIMEOUT";
        public const string DependencyFailed = "STAT-STARTUP-DEPENDENCY-FAILED";
        public const string NotCompleted = "STAT-STARTUP-NOT-COMPLETED";
    }

    public static class Messages
    {
        public const string MSG_TIMEZONE_INVALID =
            "STAT-TIMEZONE-INVALID: Statistics timezone must be Asia/Ho_Chi_Minh for the Sprint 3 contract.";
        public const string MSG_METRIC_MAPPER_DUPLICATE =
            "STAT-METRIC-MAPPER-DUPLICATE: Mapping key '{0}' is registered more than once.";
        public const string MSG_MONGO_COLLECTION_MISSING =
            "STAT-MONGO-COLLECTION-MISSING: History collection '{0}' was not found.";
        public const string MSG_MONGO_INDEX_MISSING =
            "STAT-MONGO-INDEX-MISSING: Required history indexes are missing: {0}.";
        public const string MSG_MONGO_CURSOR_INDEX_MISSING =
            "STAT-MONGO-CURSOR-INDEX-MISSING: History index '{0}' must contain persistedAtUtc ASC, eventId ASC.";
        public const string MSG_MONGO_RANGE_INVALID =
            "STAT-MONGO-RANGE-INVALID: The persisted time range must be ordered.";
        public const string MSG_SQL_TARGET_UNVERIFIED =
            "STAT-SQL-TARGET-UNVERIFIED: SQL target verification returned no result.";
        public const string MSG_SQL_DATABASE_MISMATCH =
            "STAT-SQL-DATABASE-MISMATCH: Connected to '{0}', expected '{1}'.";
        public const string MSG_SQL_SCHEMA_MISSING =
            "STAT-SQL-SCHEMA-MISSING: Schema '{0}' was not found in database '{1}'.";
        public const string MSG_SQL_SCHEMA_MISSING_WITHOUT_DATABASE =
            "STAT-SQL-SCHEMA-MISSING: Schema '{0}' was not found.";
        public const string MSG_SQL_MIGRATION_MISSING =
            "STAT-SQL-MIGRATION-MISSING: Expected migration '{0}' was not applied.";
        public const string MSG_SQL_TABLES_MISSING =
            "STAT-SQL-TABLES-MISSING: Required tables are missing: {0}.";
        public const string MSG_SQL_TYPES_MISSING =
            "STAT-SQL-TYPES-MISSING: Required table types are missing: {0}.";
        public const string MSG_SQL_METRIC_REGISTRY_MISSING =
            "STAT-SQL-METRIC-REGISTRY-MISSING: Metric set version 1 has not been seeded.";
        public const string MSG_SQL_SESSION_COMPLETED =
            "STAT-SQL-SESSION-COMPLETED: The SQL projection session is already completed.";
        public const string MSG_SQL_CHECKPOINT_CREATE_FAILED =
            "STAT-CHECKPOINT-CREATE-FAILED: Checkpoint was not created.";
        public const string MSG_SQL_CHECKPOINT_IDENTITY_MISMATCH =
            "STAT-CHECKPOINT-IDENTITY-MISMATCH: Checkpoint and lease identities must match.";
        public const string MSG_SQL_LEASE_IDENTITY_MISMATCH =
            "STAT-LEASE-IDENTITY-MISMATCH: Lease identity does not match operation identity.";
        public const string MSG_SQL_LEASE_APPLOCK_UNAVAILABLE =
            "STAT-LEASE-APPLOCK-UNAVAILABLE: Projection writer gate is currently held.";
        public const string MSG_SQL_LEASE_MUTATION_FAILED =
            "STAT-LEASE-MUTATION-FAILED: Lease mutation affected no checkpoint.";
        public const string MSG_PROJECTION_BATCH_INVALID =
            "STAT-PROJECTION-BATCH-INVALID: Projection batch contains an invalid or conflicting event outcome.";
        public const string MSG_PROJECTION_CONTRIBUTION_LIMIT =
            "STAT-PROJECTION-CONTRIBUTION-LIMIT: Projection batch contains {0} contributions; configured limit is {1}.";
        public const string MSG_PROJECTION_CHECKPOINT_CONFLICT =
            "STAT-PROJECTION-CHECKPOINT-CONFLICT: Projection checkpoint could not be advanced for the active lease.";
        public const string MSG_STATE_BUCKET_REQUIRED =
            "STAT-STATE-BUCKET-REQUIRED: State duration calculation requires at least one bucket.";
        public const string MSG_STATE_CURSOR_TYPE_INVALID =
            "STAT-STATE-CURSOR-TYPE-INVALID: State cursor type is not supported.";
        public const string MSG_STATE_OBSERVATION_INVALID =
            "STAT-STATE-OBSERVATION-INVALID: State observation contract is invalid.";
        public const string MSG_STATE_STREAM_MISMATCH =
            "STAT-STATE-STREAM-MISMATCH: State observations must belong to the cursor stream.";
        public const string MSG_PROJECTION_METRIC_KEY_MISSING =
            "STAT-PROJECTION-METRIC-KEY-MISSING: Metric definitions are missing for metric codes: {0}.";
        public const string MSG_PROJECTION_COVERAGE_START_MISSING =
            "STAT-PROJECTION-COVERAGE-START-MISSING: Incremental projection requires CoverageStartAtUtc when no stored definition resolver is configured.";
        public const string MSG_RECONCILIATION_REQUEST_INVALID =
            "STAT-RECONCILIATION-REQUEST-INVALID: Reconciliation request range or identity is invalid.";
        public const string MSG_RECONCILIATION_CLAIM_CONFLICT =
            "STAT-RECONCILIATION-CLAIM-CONFLICT: Reconciliation request claim is no longer owned by the active lease.";
        public const string MSG_RECONCILIATION_COVERAGE_UNAVAILABLE =
            "STAT-RECONCILIATION-COVERAGE-UNAVAILABLE: Source coverage is insufficient for the requested reconciliation range. Reason={0}.";
        public const string MSG_RECONCILIATION_REVISION_STALE =
            "STAT-RECONCILIATION-REVISION-STALE: Reconciliation snapshot revision is stale and cannot be published.";
        public const string MSG_RECONCILIATION_MAX_RANGE_EXCEEDED =
            "STAT-RECONCILIATION-RANGE-EXCEEDED: Reconciliation range exceeds the configured maximum.";
        public const string MSG_RECONCILIATION_RUN_FAILED =
            "STAT-RECONCILIATION-RUN-FAILED: Exact reconciliation run failed.";
        public const string MSG_RECONCILIATION_RANGE_INVALID =
            "STAT-RECONCILIATION-RANGE-INVALID: The propagation range must be ordered.";
        public const string MSG_RECONCILIATION_SOURCE_IDENTITY_MISSING =
            "STAT-RECONCILIATION-SOURCE-IDENTITY-MISSING: A staged source identity was not found in the retained history source.";
        public const string MSG_RECOVERY_DEFINITION_MISSING =
            "STAT-RECOVERY-DEFINITION-MISSING: Projection definition '{0}' version {1} was not found.";
        public const string MSG_RECOVERY_DEFINITION_CONFLICT =
            "STAT-RECOVERY-DEFINITION-CONFLICT: Projection definition '{0}' version {1} does not match the requested immutable contract.";
        public const string MSG_RECOVERY_RUN_CONFLICT =
            "STAT-RECOVERY-RUN-CONFLICT: An active recovery run already exists for projection '{0}' version {1}.";
        public const string MSG_LOG_RECONCILIATION_COMPLETED =
            "Statistics reconciliation completed. RunId={RunId}, FromDate={FromDate}, ToDate={ToDate}, AffectedRows={AffectedRows}.";
        public const string MSG_LOG_RECONCILIATION_CYCLE =
            "Statistics reconciliation cycle completed. Completed={Completed}, Retried={Retried}, Failed={Failed}, CompletedAtUtc={CompletedAtUtc}.";
        public const string MSG_LOG_RECONCILIATION_RETRY =
            "Statistics reconciliation will retry. RequestId={RequestId}, Attempt={Attempt}, Reason={Reason}.";
        public const string MSG_LOG_MANUAL_MODE_STARTED =
            "Statistics manual mode started. Mode={Mode}, ProjectionVersion={ProjectionVersion}, FromDate={FromDate}, ToDate={ToDate}.";
        public const string MSG_LOG_MANUAL_MODE_COMPLETED =
            "Statistics manual mode completed. Mode={Mode}, Completed={Completed}, Retried={Retried}, Failed={Failed}.";
        public const string MSG_LOG_MANUAL_MODE_SKIPPED =
            "Statistics manual mode skipped because the projection definition is already ready. ProjectionVersion={ProjectionVersion}.";
        public const string MSG_LOG_RETENTION_CLEANUP =
            "Statistics operational cleanup completed. DeletedStagingRows={DeletedStagingRows}, DeletedProjectionRuns={DeletedProjectionRuns}.";
        public const string MSG_LOG_PROJECTION_LEASE_ACQUIRED =
            "Statistics projection lease acquired. Epoch={Epoch}, ExpiresAtUtc={ExpiresAtUtc}.";
        public const string MSG_LOG_PROJECTION_LEASE_UNAVAILABLE =
            "Statistics projection lease is held by another worker; retrying.";
        public const string MSG_LOG_PROJECTION_BATCH_COMMITTED =
            "Statistics projection batch committed. Read={ReadCount}, New={NewCount}, Duplicate={DuplicateCount}, AffectedRows={AffectedRows}, DataRevision={DataRevision}.";
        public const string MSG_LOG_PROJECTION_FAILED =
            "Statistics projection batch failed; checkpoint was not advanced.";
        public const string MSG_LOG_STATE_REFRESH_COMPLETED =
            "Statistics state duration refresh committed. AffectedRows={AffectedRows}.";
        public const string MSG_EVENT_ID_INVALID =
            "STAT-EVENT-ID-INVALID: Event identity must be 64 lowercase hexadecimal characters.";
        public const string MSG_STARTUP_CANCELLED =
            "STAT-STARTUP-CANCELLED: Startup preflight was cancelled.";
        public const string MSG_HEALTH_DISABLED = "Statistics worker is disabled.";
        public const string MSG_HEALTH_READY = "Startup preflight completed.";
        public const string MSG_HEALTH_NOT_READY =
            "Statistics worker is not ready. FailureCode={0}";
        public const string MSG_LOG_WORKER_DISABLED =
            "Statistics worker is disabled; no projection loop will be started.";
        public const string MSG_LOG_HOST_STOPPING_DISABLED =
            "Statistics worker is disabled; stopping the host without opening a processing loop.";
        public const string MSG_LOG_STARTUP_READY =
            "Statistics startup preflight completed successfully.";
        public const string MSG_LOG_STARTUP_FAILED =
            "Statistics startup preflight failed. FailureCode={FailureCode}";
        public const string MSG_LOG_CONFIGURATION_VALIDATED =
            "Statistics configuration validated. Enabled={Enabled}, WorkerId={WorkerId}, Mode={Mode}, ProjectionName={ProjectionName}, ProjectionVersion={ProjectionVersion}, MongoConnectionStringConfigured={MongoConnectionStringConfigured}, MongoDatabase={MongoDatabase}, HistoryCollection={HistoryCollection}, SqlConnectionStringConfigured={SqlConnectionStringConfigured}, SqlDatabase={SqlDatabase}, SqlSchema={SqlSchema}, CompanyScopeCount={CompanyScopeCount}, DeviceScopeCount={DeviceScopeCount}";

        public static string Format(string template, params object?[] arguments) =>
            string.Format(CultureInfo.InvariantCulture, template, arguments);
    }
}
