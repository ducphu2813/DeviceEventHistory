namespace DeviceEventHistory.Domain.Common;

/// <summary>
/// Shared, non-secret application contract values.
/// Runtime values must continue to come from configuration or secret providers.
/// </summary>
public static class AppConst
{
    public static class Configuration
    {
        public const string RootSection = "DeviceEventHistory";
        public const string RawLogSection = RootSection + ":RawLog";
        public const string MongoDbSection = RootSection + ":MongoDb";
        public const string IngestionSection = RootSection + ":Ingestion";
    }

    public static class EnvironmentVariables
    {
        public const string MongoDbConnectionString = "DEVICE_EVENT_HISTORY_MONGODB_CONNECTION_STRING";
    }

    public static class Defaults
    {
        public const bool WorkerEnabled = false;
        public const string WorkerId = "device-event-history-worker-01";
        public const int PollIntervalSeconds = 2;
        public const int ReadBufferBytes = 512 * 1024;
        public const int MaxRecordBytes = 1024 * 1024;
        public const int LookbackDays = 1;
        public const int MaxConcurrentFiles = 4;
        public const int DefaultRetentionDays = 90;
        public const int FailureRetentionDays = 30;
        public const int PersistenceRetryCount = 5;
        public const int ShutdownTimeoutSeconds = 30;
        public const int MaxRawPayloadBytes = 1024 * 1024;
    }

    public static class RawLog
    {
        public const string DefaultTimeZoneId = "SE Asia Standard Time";
        public const string DefaultFilePattern = "File_*.txt";
        public const string FilePatternRegex = @"^File_[A-Za-z0-9_?*.-]+\.txt$";
        public const string RecordTerminator = "e(0)";
    }

    public static class MongoDb
    {
        public const string DefaultDatabaseName = "device_event_history";
        public const string HistoryCollection = "device_event_history";
        public const string FailureCollection = "ingestion_failures";
        public const string CheckpointCollection = "ingestion_checkpoints";
        public const string SystemCollectionPrefix = "system.";
        public const int MaxCollectionNameLength = 120;
    }

    public static class Path
    {
        public const string ParentDirectorySegment = "..";
    }

    public static class Logging
    {
        public const string StartupCategory = "Startup";
        public const string ConfigurationValidatedMessage =
            "Device Event History configuration validated.";
        public const string WorkerDisabledMessage =
            "Device Event History Worker is disabled by configuration.";
        public const string IngestionNotImplementedMessage =
            "Worker is enabled, but the raw-log ingestion pipeline is not implemented in this work package.";
    }

    public static class Messages
    {
        public const string MSG_WORKER_ID_REQUIRED =
            "WorkerId is required when the Worker is enabled.";
        public const string MSG_DEFAULT_RETENTION_DAYS_POSITIVE =
            "DefaultRetentionDays must be greater than zero.";
        public const string MSG_FAILURE_RETENTION_DAYS_POSITIVE =
            "FailureRetentionDays must be greater than zero.";
        public const string MSG_PERSISTENCE_RETRY_COUNT_NON_NEGATIVE =
            "PersistenceRetryCount cannot be negative.";
        public const string MSG_SHUTDOWN_TIMEOUT_POSITIVE =
            "ShutdownTimeout must be greater than zero.";
        public const string MSG_MAX_RAW_PAYLOAD_BYTES_POSITIVE =
            "MaxRawPayloadBytes must be greater than zero.";
        public const string MSG_POLL_INTERVAL_POSITIVE =
            "PollInterval must be greater than zero.";
        public const string MSG_READ_BUFFER_BYTES_POSITIVE =
            "ReadBufferBytes must be greater than zero.";
        public const string MSG_MAX_RECORD_BYTES_POSITIVE =
            "MaxRecordBytes must be greater than zero.";
        public const string MSG_LOOKBACK_DAYS_NON_NEGATIVE =
            "LookbackDays cannot be negative.";
        public const string MSG_MAX_CONCURRENT_FILES_POSITIVE =
            "MaxConcurrentFiles must be greater than zero.";
        public const string MSG_SOURCES_REQUIRED =
            "At least one raw-log source is required when the Worker is enabled.";
        public const string MSG_SOURCE_ID_REQUIRED =
            "{0}.SourceId is required.";
        public const string MSG_SOURCE_ID_DUPLICATED =
            "{0}.SourceId '{1}' is duplicated.";
        public const string MSG_COMPANY_ID_POSITIVE =
            "{0}.CompanyId must be greater than zero.";
        public const string MSG_ROOT_PATH_ABSOLUTE =
            "{0}.RootPath must be an absolute path.";
        public const string MSG_ROOT_PATH_NO_PARENT_DIRECTORY =
            "{0}.RootPath cannot contain a parent-directory segment ('..').";
        public const string MSG_ROOT_PATH_INVALID =
            "{0}.RootPath is not a valid path.";
        public const string MSG_TIME_ZONE_REQUIRED =
            "{0}.TimeZoneId is required.";
        public const string MSG_TIME_ZONE_NOT_FOUND =
            "{0}.TimeZoneId '{1}' was not found on this system.";
        public const string MSG_TIME_ZONE_INVALID =
            "{0}.TimeZoneId '{1}' is invalid.";
        public const string MSG_FILE_PATTERN_SAFE =
            "{0}.FilePattern must be a safe {1} file-name pattern without path traversal.";
        public const string MSG_POLICY_UNSUPPORTED =
            "{0} has an unsupported value.";
        public const string MSG_CONNECTION_STRING_REQUIRED =
            "ConnectionString is required through configuration or the configured environment variable.";
        public const string MSG_MONGO_DATABASE_NAME_INVALID =
            "{0} contains invalid MongoDB database-name characters.";
        public const string MSG_MONGO_COLLECTION_NAME_INVALID =
            "{0} is not a valid MongoDB collection name.";
    }
}
