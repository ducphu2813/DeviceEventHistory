using System.Globalization;

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
        public const string DatabaseSettingsSection = RootSection + ":DatabaseSettings";
        public const string MongoDbSection = DatabaseSettingsSection + ":MongoDb";
        public const string IngestionSection = RootSection + ":Ingestion";
        public const string ObservabilitySection = RootSection + ":Observability";
        public const string AppHubSection = RootSection + ":AppHub";
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
        public const int MaxBytesPerTurn = 2 * 1024 * 1024;
        public const int MaxRecordsPerTurn = 1_000;
        public const int MaxTurnDurationMilliseconds = 250;
        public const int SchedulerQueueMultiplier = 4;
        public const int RemoteRequestTimeoutSeconds = 30;
        public const int DefaultRetentionDays = 90;
        public const int FailureRetentionDays = 30;
        public const int PersistenceRetryCount = 5;
        public const int PersistenceRetryDelayMilliseconds = 250;
        public const int PersistenceRetryMaxDelayMilliseconds = 4_000;
        public const int ShutdownTimeoutSeconds = 30;
        public const int MaxRawPayloadBytes = 1024 * 1024;
        public const int MongoFailureUnhealthyThreshold = 3;
        public const int SourceFailureUnhealthyThreshold = 3;
        public const int ProgressStaleMinutes = 5;
        public const int AppHubChannelCapacity = 5_000;
        public const int AppHubEnqueueTimeoutMilliseconds = 100;
        public const int AppHubReconnectMinDelaySeconds = 1;
        public const int AppHubReconnectMaxDelaySeconds = 30;
    }

    public static class RawLog
    {
        public const int SchemaVersion = 1;
        public const string ParserVersion = "1.0";
        public const string Producer = "RFID.Antenna";
        public const string SourceKind = "rfid_antenna_file";
        public const string PayloadFormat = "rfid-raw-v1";
        public const string RecordEventName = "raw_record";
        public const string RelativePathDateFormat = "yyyy/MM/dd";
        public const string HeaderBlock = "@";
        public const string GateStateBlock = "b";
        public const string SignalBlock = "t";
        public const string BusinessEventBlock = "te";
        public const string StyleProcessBlock = "sp";
        public const string UserBlock = "u";
        public const int HeaderFieldCount = 4;
        public const int GateStateFieldCount = 1;
        public const int SignalFieldCount = 9;
        public const int BusinessEventFieldCount = 5;
        public const int StyleProcessFieldCount = 1;
        public const int UserFieldCount = 1;
        public const char ProcessListSeparator = '-';
        public const string HeaderTimeFormat = "hh\\:mm\\:ss";
        public const string HeaderTimeShortFormat = "h\\:mm\\:ss";
        public const string SignalDateTimeFormat = "dd/MM/yyyy HH:mm:ss";
        public const string SignalDateTimeShortFormat = "d/M/yyyy H:mm:ss";
        public const string DefaultTimeZoneId = "SE Asia Standard Time";
        public const string DefaultFilePattern = "File_*.txt";
        public const string FilePatternRegex = @"^File_[A-Za-z0-9_?*.-]+\.txt$";
        public const string RecordTerminator = "e(0)";
        public const string FileNameRegex = @"^File_(?<fileId>\d+)\.txt$";
    }

    public static class AppHub
    {
        public const string Producer = "ERP.AppHub";
        public const string SourceKind = SourceKinds.ErpAppHub;
        public const string Transport = SourceTransports.ClassicSignalR;
        public const string DeliveryKind = DeliveryKinds.Realtime;
        public const string ParserVersion = "erp-apphub-v1";
        public const string PayloadFormat = "signalr-arguments-json-v1";
        public const string DefaultHubName = "AppHub";
        public const string JoinMonitoringMethod = "JoinMonitoring";

        public static class Callbacks
        {
            public const string ReceiveDeviceOnline = "receiveDeviceOnline";
            public const string ReceiveStateConnected = "receiveStateConnected";
            public const string ReceiveGreenState = "receiveGreenState";
            public const string ReceiveRedState = "receiveRedState";
            public const string ReceiveTimeSensor = "receiveTimeSensor";
            public const string ReceiveDeviceReadTag = "receiveDeviceReadTag";
            public const string ReceiveDeviceScanConnect = "receiveDeviceScanConnect";
            public const string ReceiveDeviceScanDisconnect = "receiveDeviceScanDisconnect";
            public const string ReceiveClientDeviceConnected = "receiveClientDeviceConnected";
            public const string ReceiveClientDeviceDisconnected = "receiveClientDeviceDisconnected";
            public const string ReceiveRequestDeviceScanInfoOnline = "receiveRequestDeviceScanInfoOnline";

            public static IReadOnlySet<string> Registered { get; } =
                new HashSet<string>(StringComparer.Ordinal)
                {
                    ReceiveDeviceOnline,
                    ReceiveStateConnected,
                    ReceiveGreenState,
                    ReceiveRedState,
                    ReceiveTimeSensor,
                    ReceiveDeviceReadTag,
                    ReceiveDeviceScanConnect,
                    ReceiveDeviceScanDisconnect,
                    ReceiveClientDeviceConnected,
                    ReceiveClientDeviceDisconnected,
                    ReceiveRequestDeviceScanInfoOnline
                };
        }
    }

    public static class MongoDb
    {
        public const string DefaultDatabaseName = "device_event_history";
        public const string HistoryCollection = "device_event_history";
        public const string FailureCollection = "ingestion_failures";
        public const string CheckpointCollection = "ingestion_checkpoints";
        public const string CheckpointKeySeparator = "|";
        public const string CheckpointDateFormat = "yyyy-MM-dd";
        public const string EventIdIndexName = "ux_event_id";
        public const string FailureIdIndexName = "ux_failure_id";
        public const string HistoryOccurredAtIndexName = "ix_occurred_at_utc_desc";
        public const string HistoryDeviceOccurredAtIndexName = "ix_device_occurred_at_utc_desc";
        public const string HistoryGateOccurredAtIndexName = "ix_gate_occurred_at_utc_desc";
        public const string HistoryTagOccurredAtIndexName = "ix_tag_occurred_at_utc_desc";
        public const string HistoryCategoryOccurredAtIndexName = "ix_category_occurred_at_utc_desc";
        public const string HistoryParseReceivedAtIndexName = "ix_parse_received_at_utc_desc";
        public const string HistorySourceOffsetIndexName = "ix_source_file_offset";
        public const string FailureSourceOffsetIndexName = "ix_source_file_offset";
        public const string FailureCodeReceivedAtIndexName = "ix_error_code_received_at_utc_desc";
        public const string FailureResolvedAtIndexName = "ix_resolved_at_utc";
        public const string CheckpointSourceIdentityIndexName = "ux_source_folder_file_path";
        public const string CheckpointUpdatedAtIndexName = "ix_updated_at_utc_desc";
        public const string SystemCollectionPrefix = "system.";
        public const int MaxCollectionNameLength = 120;
    }

    public static class Identity
    {
        public const string EventPrefix = "event";
        public const string FailurePrefix = "failure";
        public const string Separator = "|";
        public const string IsoDateTimeFormat = "O";
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
        public const string MongoIndexesInitializedMessage =
            "MongoDB collections and indexes initialized.";
        public const string IngestionStartedMessage =
            "Raw-log ingestion orchestration started.";
        public const string IngestionStoppedMessage =
            "Raw-log ingestion orchestration stopped.";
        public const string AppHubIngestionStartedMessage =
            "ERP AppHub ingestion orchestration started with SourceCount={SourceCount}.";
        public const string AppHubIngestionStoppedMessage =
            "ERP AppHub ingestion orchestration stopped.";
        public const string AppHubSourceConnectionFailedMessage =
            "ERP AppHub source connection failed for SourceId={SourceId}.";
        public const string AppHubSourceConnectedMessage =
            "ERP AppHub source connected and joined Monitoring for SourceId={SourceId}, Generation={Generation}.";
        public const string AppHubSourceDisconnectedMessage =
            "ERP AppHub source disconnected for SourceId={SourceId}.";
        public const string AppHubCallbackDroppedMessage =
            "ERP AppHub callback was dropped after bounded admission for SourceId={SourceId}, EventName={EventName}, Reason={Reason}.";
        public const string AppHubCallbackProcessingFailedMessage =
            "ERP AppHub callback processing failed for SourceId={SourceId}, EventName={EventName}.";
        public const string AppHubChannelDrainTimeoutMessage =
            "ERP AppHub channel drain timed out for SourceId={SourceId}, RemainingCount={RemainingCount}.";
        public const string AppHubChannelDrainedMessage =
            "ERP AppHub channel drained for SourceId={SourceId}, ProcessedCount={ProcessedCount}.";
        public const string SchedulerStartedMessage =
            "Raw-log scheduler started with ConsumerCount={ConsumerCount}.";
        public const string FileTurnStartedMessage =
            "Raw-log file turn started for SourceId={SourceId}, FileId={FileId}, CheckpointPosition={CheckpointPosition}, ReadOffset={ReadOffset}.";
        public const string SourceDiscoveryFailedMessage =
            "Raw-log source discovery failed for SourceId={SourceId}.";
        public const string FileStateInitializationFailedMessage =
            "Raw-log file state initialization failed for SourceId={SourceId}, FileId={FileId}, FolderDate={FolderDate}.";
        public const string FileProcessingFailedMessage =
            "Raw-log file turn failed for SourceId={SourceId}, FileId={FileId}, FolderDate={FolderDate}, Position={Position}.";
        public const string FileTruncatedMessage =
            "Raw-log file was truncated or replaced; processing stopped for SourceId={SourceId}, FileId={FileId}, FolderDate={FolderDate}, Position={Position}.";
        public const string FileCheckpointConflictMessage =
            "Raw-log checkpoint conflict detected for SourceId={SourceId}, FileId={FileId}, FolderDate={FolderDate}.";
        public const string FileTurnStoppedMessage =
            "Raw-log file processing stopped for SourceId={SourceId}, FileId={FileId}, FolderDate={FolderDate}.";
        public const string SourceDiscoveryCompletedMessage =
            "Raw-log source discovery completed for SourceId={SourceId}, Mode={Mode}, FileCount={FileCount}.";
        public const string FileStateCreatedMessage =
            "Raw-log file state created for SourceId={SourceId}, FileId={FileId}, StartPosition={StartPosition}, StartupExistingFile={StartupExistingFile}, Policy={Policy}.";
        public const string FileTurnReadMessage =
            "Raw-log turn read SourceId={SourceId}, FileId={FileId}, Offset={Offset}, BytesRead={BytesRead}, NextOffset={NextOffset}, FileLength={FileLength}, HasMore={HasMore}, PendingBytes={PendingBytes}.";
        public const string FileTurnCompletedMessage =
            "Raw-log turn completed for SourceId={SourceId}, FileId={FileId}, Status={Status}, CheckpointPosition={CheckpointPosition}, ReadOffset={ReadOffset}, ReadyRecords={ReadyRecords}, PendingBytes={PendingBytes}.";
        public const string FileRequeueDeferredMessage =
            "Raw-log file requeue deferred because the scheduler queue is full for SourceId={SourceId}, FileId={FileId}.";
        public const string RemoteDirectoryDiscoveredMessage =
            "Remote raw-log directory discovered for SourceId={SourceId}, FolderDate={FolderDate}, FileCount={FileCount}.";
        public const string FileRecordProcessedMessage =
            "Raw-log record processed with Result={Result}, OffsetStart={OffsetStart}, OffsetEnd={OffsetEnd}.";
    }

    public static class Messages
    {
        public static string Format(string message, params object[] arguments) =>
            string.Format(CultureInfo.InvariantCulture, message, arguments);

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
        public const string MSG_MAX_BYTES_PER_TURN_POSITIVE =
            "MaxBytesPerTurn must be greater than zero.";
        public const string MSG_MAX_RECORDS_PER_TURN_POSITIVE =
            "MaxRecordsPerTurn must be greater than zero.";
        public const string MSG_MAX_TURN_DURATION_POSITIVE =
            "MaxTurnDuration must be greater than zero.";
        public const string MSG_MONGO_FAILURE_UNHEALTHY_THRESHOLD_POSITIVE =
            "MongoFailureUnhealthyThreshold must be greater than zero.";
        public const string MSG_SOURCE_FAILURE_UNHEALTHY_THRESHOLD_POSITIVE =
            "SourceFailureUnhealthyThreshold must be greater than zero.";
        public const string MSG_PROGRESS_STALE_AFTER_POSITIVE =
            "ProgressStaleAfter must be greater than zero.";
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
        public const string MSG_SOURCE_MODE_UNSUPPORTED =
            "{0}.Mode has an unsupported value.";
        public const string MSG_REMOTE_BASE_URL_REQUIRED =
            "{0}.RemoteBaseUrl is required when Mode is RemoteHttp.";
        public const string MSG_REMOTE_BASE_URL_INVALID =
            "{0}.RemoteBaseUrl must be an absolute HTTP or HTTPS URL without query or fragment.";
        public const string MSG_POLICY_UNSUPPORTED =
            "{0} has an unsupported value.";
        public const string MSG_CONNECTION_STRING_REQUIRED =
            "ConnectionString is required through configuration or the configured environment variable.";
        public const string MSG_MONGO_DATABASE_NAME_INVALID =
            "{0} contains invalid MongoDB database-name characters.";
        public const string MSG_MONGO_COLLECTION_NAME_INVALID =
            "{0} is not a valid MongoDB collection name.";
        public const string MSG_CHECKPOINT_POSITION_INVALID =
            "Checkpoint position cannot be negative.";
        public const string MSG_CHECKPOINT_POSITION_REGRESSION =
            "Checkpoint position cannot move backwards from {0} to {1}.";
        public const string MSG_CHECKPOINT_ADVANCE_CONFLICT =
            "Checkpoint version conflict was detected for '{0}'.";
        public const string MSG_PERSISTENCE_OUTCOME_REQUIRED =
            "A raw record processing result must contain an event or failure.";
        public const string MSG_CANONICAL_INGESTION_OUTCOME_REQUIRED =
            "A canonical ingestion result must contain an event or failure.";
        public const string MSG_CANONICAL_INGESTION_OUTCOME_EXCLUSIVE =
            "A canonical ingestion result cannot contain both an event and a failure.";
        public const string MSG_RAW_SOURCE_EVENT_MAPPER_KEY_DUPLICATED =
            "A raw source event mapper is already registered for key '{0}'.";
        public const string MSG_RAW_SOURCE_EVENT_UNMAPPED =
            "No canonical mapper is registered for source kind '{0}' and event '{1}'.";
        public const string MSG_APPHUB_SOURCES_REQUIRED =
            "At least one AppHub source is required when AppHub is enabled.";
        public const string MSG_APPHUB_SOURCE_ID_REQUIRED =
            "{0}.SourceId is required.";
        public const string MSG_APPHUB_SOURCE_ID_DUPLICATED =
            "{0}.SourceId '{1}' is duplicated across ingestion sources.";
        public const string MSG_APPHUB_ENDPOINT_REQUIRED =
            "{0}.Endpoint is required.";
        public const string MSG_APPHUB_ENDPOINT_INVALID =
            "{0}.Endpoint must be an absolute HTTP or HTTPS URL without user-info, query or fragment.";
        public const string MSG_APPHUB_HUB_NAME_REQUIRED =
            "{0}.HubName is required.";
        public const string MSG_APPHUB_HUB_NAME_INVALID =
            "{0}.HubName contains unsupported characters.";
        public const string MSG_APPHUB_COMPANY_ID_POSITIVE =
            "{0}.CompanyId must be null or greater than zero.";
        public const string MSG_APPHUB_DEDICATED_COMPANY_REQUIRED =
            "{0}.CompanyId must be greater than zero when DedicatedSingleTenant is enabled.";
        public const string MSG_APPHUB_CHANNEL_CAPACITY_POSITIVE =
            "{0}.ChannelCapacity must be greater than zero.";
        public const string MSG_APPHUB_ENQUEUE_TIMEOUT_POSITIVE =
            "{0}.EnqueueTimeout must be greater than zero.";
        public const string MSG_APPHUB_RECONNECT_MIN_DELAY_POSITIVE =
            "{0}.ReconnectMinDelay must be greater than zero.";
        public const string MSG_APPHUB_RECONNECT_MAX_DELAY_POSITIVE =
            "{0}.ReconnectMaxDelay must be greater than zero.";
        public const string MSG_APPHUB_RECONNECT_DELAY_RANGE_INVALID =
            "{0}.ReconnectMinDelay cannot be greater than ReconnectMaxDelay.";
        public const string MSG_APPHUB_ENABLED_EVENTS_REQUIRED =
            "{0}.EnabledEvents must contain at least one registered callback.";
        public const string MSG_APPHUB_EVENT_UNSUPPORTED =
            "{0}.EnabledEvents contains unsupported callback '{1}'.";
        public const string MSG_APPHUB_EVENT_DUPLICATED =
            "{0}.EnabledEvents contains duplicate callback '{1}'.";
        public const string MSG_APPHUB_CREDENTIAL_ENVIRONMENT_VARIABLE_REQUIRED =
            "{0} must configure AccessTokenEnvironmentVariable or TokenJwtEnvironmentVariable.";
        public const string MSG_APPHUB_CREDENTIAL_VALUE_REQUIRED =
            "No configured AppHub service credential was found in the approved environment variable.";
        public const string MSG_APPHUB_PAYLOAD_TOO_LARGE =
            "AppHub callback payload size {0} bytes exceeds the configured maximum of {1} bytes.";
        public const string MSG_APPHUB_RUNTIME_SOURCE_REQUIRED =
            "An AppHub source is required to create a source runtime.";
        public const string MSG_APPHUB_RUNTIME_ALREADY_STARTED =
            "The AppHub source runtime has already started.";
        public const string MSG_APPHUB_CALLBACK_NAME_REQUIRED =
            "AppHub callback name is required.";
        public const string MSG_APPHUB_CALLBACK_REGISTERED_AFTER_START =
            "AppHub callbacks must be registered before the connection starts.";
        public const string MSG_APPHUB_CONNECTION_ALREADY_STARTED =
            "The AppHub connection has already started.";
        public const string MSG_APPHUB_PROXY_REQUIRED =
            "The AppHub hub proxy is not initialized.";
        public const string MSG_RAW_RECORD_FILE_SOURCE_CONTEXT_REQUIRED =
            "Raw-record persistence requires a complete file source context.";
        public const string MSG_RAW_LOG_FAILURE_COMPANY_ID_REQUIRED =
            "Raw-log failure persistence requires a company ID.";
        public const string MSG_RAW_LOG_FAILURE_FILE_ID_REQUIRED =
            "Raw-log failure persistence requires a file ID.";
        public const string MSG_RAW_LOG_FAILURE_FILE_NAME_REQUIRED =
            "Raw-log failure persistence requires a file name.";
        public const string MSG_RAW_LOG_FAILURE_RELATIVE_PATH_REQUIRED =
            "Raw-log failure persistence requires a relative path.";
        public const string MSG_RAW_LOG_FAILURE_FOLDER_DATE_REQUIRED =
            "Raw-log failure persistence requires a folder date.";
        public const string MSG_RAW_LOG_FAILURE_START_OFFSET_REQUIRED =
            "Raw-log failure persistence requires a start offset.";
        public const string MSG_RAW_LOG_FAILURE_END_OFFSET_REQUIRED =
            "Raw-log failure persistence requires an end offset.";
        public const string MSG_RAW_LOG_FAILURE_RAW_TEXT_REQUIRED =
            "Raw-log failure persistence requires raw text.";
        public const string MSG_CHECKPOINT_CONFIRMATION_REQUIRED =
            "Checkpoint advance was not confirmed after persistence.";
        public const string MSG_RAW_LOG_STATE_STOPPED =
            "Raw-log file state is stopped and cannot process another turn.";
        public const string MSG_NO_RAW_LOG_DISCOVERY_ADAPTER =
            "No raw-log discovery adapter is registered for mode '{0}'.";
        public const string MSG_RAW_LOG_CHUNK_NOT_CONTIGUOUS =
            "The incoming chunk is not contiguous with the pending raw-log bytes.";
        public const string MSG_RAW_LOG_RECORD_TOO_LARGE =
            "A raw-log record exceeded the configured maximum of {0} bytes.";
        public const string MSG_NO_RAW_LOG_TAIL_READER =
            "No raw-log tail reader is registered for mode '{0}'.";
        public const string MSG_REMOTE_RANGE_REQUEST_IGNORED =
            "Remote raw-log server ignored the byte range request for '{0}'.";
        public const string MSG_RAW_RECORD_HEADER_REQUIRED =
            "The raw record must contain exactly one valid '@(...)' header block.";
        public const string MSG_RAW_BLOCK_MALFORMED =
            "Raw block '{0}' is malformed.";
        public const string MSG_RAW_BLOCK_FIELD_COUNT =
            "Raw block '{0}' must contain {1} fields but contains {2}.";
        public const string MSG_RAW_BLOCK_FIELD_INVALID =
            "Raw block '{0}' field {1} has an invalid value: '{2}'.";
        public const string MSG_RAW_BLOCK_UNKNOWN =
            "Unknown raw block '{0}' was preserved without canonical mapping.";
        public const string MSG_RAW_TIME_ZONE_INVALID =
            "Source time zone '{0}' is unavailable for raw timestamp conversion.";
    }

    public static class Categories
    {
        public const string TagRead = "tag_read";
        public const string BusinessProcess = "business_process";
        public const string GateState = "gate_state";
        public const string DeviceOnline = "device_online";
        public const string DeviceConnection = "device_connection";
        public const string ScannerConnection = "scanner_connection";
        public const string ClientDeviceConnection = "client_device_connection";
        public const string DeviceControlState = "device_control_state";
        public const string DeviceSensorState = "device_sensor_state";
        public const string DeviceSnapshot = "device_snapshot";
        public const string DeviceError = "device_error";
        public const string ApplicationError = "application_error";
        public const string Unknown = "unknown";
    }

    public static class SourceKinds
    {
        public const string RfidAntennaFile = "rfid_antenna_file";
        public const string ErpAppHub = "erp_apphub";
        public const string DirectPublisher = "direct_publisher";
        public const string ScannerApplication = "scanner_application";
        public const string ApplicationLog = "application_log";
    }

    public static class SchemaVersions
    {
        public const int CanonicalV2 = 2;
    }

    public static class SourceTransports
    {
        public const string File = "file";
        public const string HttpRange = "http_range";
        public const string ClassicSignalR = "classic_signalr";
        public const string Http = "http";
        public const string MessageBroker = "message_broker";
        public const string ApplicationLog = "application_log";
    }

    public static class DeliveryKinds
    {
        public const string Activity = "activity";
        public const string Realtime = "realtime";
        public const string Snapshot = "snapshot";
        public const string SnapshotCandidate = "snapshot_candidate";
        public const string ReconnectSnapshot = "reconnect_snapshot";
        public const string Heartbeat = "heartbeat";
        public const string Unknown = "unknown";
    }

    public static class TimeBases
    {
        public const string Occurred = "occurred";
        public const string Received = "received";
    }

    public static class IngestionStages
    {
        public const string Admission = "admission";
        public const string Framing = "framing";
        public const string Deserialization = "deserialization";
        public const string Validation = "validation";
        public const string MetadataResolution = "metadata_resolution";
        public const string Mapping = "mapping";
        public const string PersistenceContract = "persistence_contract";
    }

    public static class Parsing
    {
        public const string StatusParsed = "parsed";
        public const string StatusParsedWithWarnings = "parsed_with_warnings";
        public const string StatusUnmapped = "unmapped";
        public const string InvalidRecordFormat = "INVALID_RECORD_FORMAT";
        public const string InvalidRawBlock = "INVALID_RAW_BLOCK";
        public const string UnknownRawBlock = "UNKNOWN_RAW_BLOCK";
        public const string InvalidSourceTimeZone = "INVALID_SOURCE_TIME_ZONE";
        public const string TenantMismatch = "TENANT_MISMATCH";
        public const string TenantUnresolved = "TENANT_UNRESOLVED";
        public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
        public const string UnknownSourceEvent = "UNKNOWN_SOURCE_EVENT";
        public const string PayloadSizeBytesDetail = "payload_size_bytes";
        public const string MaximumPayloadBytesDetail = "maximum_payload_bytes";
    }

    public static class Observability
    {
        public const string MeterName = "DeviceEventHistory.Ingestion";
        public const string MeterVersion = RawLog.ParserVersion;
        public const string MongoHealthCheckName = "mongodb";
        public const string SourceHealthCheckName = "raw-log-source";
        public const string IngestionHealthCheckName = "ingestion-progress";
        public const string HealthStatusReady = "ready";
        public const string HealthStatusDegraded = "degraded";
        public const string HealthStatusUnhealthy = "unhealthy";
        public const string HealthReasonStartupPending = "startup_pending";
        public const string HealthReasonMongoUnavailable = "mongo_unavailable";
        public const string HealthReasonSourceUnavailable = "source_unavailable";
        public const string HealthReasonFileTruncated = "file_truncated";
        public const string HealthReasonProgressStale = "progress_stale";
        public const string AppHubAdmissionEnqueueTimeout = "enqueue_timeout";
        public const string AppHubAdmissionChannelClosed = "channel_closed";
        public const string AppHubAdmissionSerializationFailed = "serialization_failed";
        public const string OperationMongo = "mongodb.operation";
        public const string OperationHistoryWrite = "history.write";
        public const string OperationFailureWrite = "failure.write";
        public const string OperationCheckpointAdvance = "checkpoint.advance";
        public const string MetricFilesDiscovered = "device_event_history.files.discovered";
        public const string MetricFilesActive = "device_event_history.files.active";
        public const string MetricSourceAccessFailures = "device_event_history.source.access_failures";
        public const string MetricBytesRead = "device_event_history.bytes.read";
        public const string MetricRecordsFramed = "device_event_history.records.framed";
        public const string MetricPartialRecords = "device_event_history.records.partial";
        public const string MetricRecordsParsed = "device_event_history.records.parsed";
        public const string MetricRecordsParseWarnings = "device_event_history.records.parse_warnings";
        public const string MetricRecordsParseFailures = "device_event_history.records.parse_failures";
        public const string MetricHistoryWrites = "device_event_history.history.writes";
        public const string MetricFailureWrites = "device_event_history.failure.writes";
        public const string MetricDuplicateIdentities = "device_event_history.persistence.duplicates";
        public const string MetricCheckpointAdvances = "device_event_history.checkpoint.advances";
        public const string MetricCheckpointFailures = "device_event_history.checkpoint.failures";
        public const string MetricMongoRetries = "device_event_history.mongodb.retries";
        public const string MetricMongoFailures = "device_event_history.mongodb.failures";
        public const string MetricPersistenceLatency = "device_event_history.persistence.duration";
        public const string MetricIngestionLagBytes = "device_event_history.ingestion.lag_bytes";
        public const string MetricOversizedRecords = "device_event_history.records.oversized";
        public const string MetricTruncatedFiles = "device_event_history.files.truncated";
        public const string MetricAppHubCallbacksReceived = "device_event_history.apphub.callbacks.received";
        public const string MetricAppHubCallbacksAdmitted = "device_event_history.apphub.callbacks.admitted";
        public const string MetricAppHubCallbacksDropped = "device_event_history.apphub.callbacks.dropped";
        public const string TagSourceId = "source_id";
        public const string TagMode = "mode";
        public const string TagFileId = "file_id";
        public const string TagStatus = "status";
        public const string TagOperation = "operation";
        public const string TagEventName = "event_name";
        public const string TagReason = "reason";
        public const string ResultCanceled = "canceled";
        public const string ResultHistory = "history";
        public const string ResultFailure = "failure";
        public const string CheckpointPolicyLabel = "Checkpoint";
        public const string HealthWorkerDisabledDescription = "Worker is disabled.";
        public const string HealthMongoUnavailableDescription = "MongoDB is unavailable.";
        public const string HealthNoSourceDescription =
            "No enabled raw-log source is configured with a valid root.";
        public const string HealthNoReadableSourceDescription =
            "No configured raw-log source is currently readable.";
        public const string HealthSourceAttentionDescription =
            "At least one raw-log source requires attention.";
        public const string HealthIngestionNotLiveDescription =
            "Ingestion loop is not live.";
        public const string HealthIngestionStatusDescription =
            "Ingestion status is {0}; reason={1}.";
    }
}
