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
