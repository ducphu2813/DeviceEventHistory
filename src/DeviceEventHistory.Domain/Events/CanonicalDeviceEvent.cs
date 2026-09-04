namespace DeviceEventHistory.Domain.Events;

public sealed record CanonicalDeviceEvent
{
    public required string EventId { get; init; }

    public required int SchemaVersion { get; init; }

    public required string Category { get; init; }

    public required string SourceKind { get; init; }

    public required int CompanyId { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public DateTimeOffset? OccurredAtLocal { get; init; }

    /// <summary>
    /// The time at which the source adapter admitted the event to ingestion.
    /// Persistence is responsible for populating this when the source does not
    /// provide it as part of the canonical event.
    /// </summary>
    public DateTimeOffset? ReceivedAtUtc { get; init; }

    /// <summary>
    /// The time at which the event was confirmed by the persistence boundary.
    /// </summary>
    public DateTimeOffset? PersistedAtUtc { get; init; }

    /// <summary>
    /// The effective timestamp used for timeline queries.
    /// </summary>
    public DateTimeOffset? TimelineAtUtc { get; init; }

    /// <summary>
    /// Indicates whether the timeline timestamp came from the source event or
    /// from ingestion receipt time.
    /// </summary>
    public string? TimeBasis { get; init; }

    public required SourceContext Source { get; init; }

    public DeviceContext? Device { get; init; }

    public required RawPayloadContext RawPayload { get; init; }

    public required FactsContext Facts { get; init; }

    public required ParseContext Parse { get; init; }

    public IngestionContext? Ingestion { get; init; }

    public sealed record SourceContext
    {
        public required string Producer { get; init; }

        public required string SourceId { get; init; }

        public string? Transport { get; init; }

        public string? EventName { get; init; }

        public string? SourceEventId { get; init; }

        public string? DeliveryKind { get; init; }

        public string? ConnectionGeneration { get; init; }

        public long? ReceiveSequence { get; init; }

        public long? FileId { get; init; }

        public string? FileName { get; init; }

        public string? RelativePath { get; init; }

        public DateOnly? FolderDate { get; init; }

        public long? OffsetStart { get; init; }

        public long? OffsetEnd { get; init; }
    }

    public sealed record DeviceContext
    {
        public int? Id { get; init; }

        public int? GateId { get; init; }

        public string? Type { get; init; }

        public string? Code { get; init; }

        public string? Name { get; init; }

        public string? GateCode { get; init; }

        public string? GateName { get; init; }
    }

    public sealed record RawPayloadContext
    {
        public required string Format { get; init; }

        public string? Text { get; init; }

        /// <summary>
        /// Ordered JSON representation of source arguments. Keeping this as
        /// JSON prevents Domain/Application from depending on SignalR,
        /// Newtonsoft.Json or BSON types.
        /// </summary>
        public string? ArgumentsJson { get; init; }

        public required string Sha256 { get; init; }

        public long? SizeBytes { get; init; }
    }

    public sealed record FactsContext
    {
        public TagReadFacts? TagRead { get; init; }

        public GateStateFacts? GateState { get; init; }

        public SignalFacts? Signal { get; init; }

        public BusinessEventFacts? BusinessEvent { get; init; }

        public StyleProcessFacts? StyleProcess { get; init; }

        public UserFacts? User { get; init; }

        public ConnectionFacts? Connection { get; init; }

        public DeviceOnlineFacts? DeviceOnline { get; init; }

        public DeviceControlStateFacts? DeviceControlState { get; init; }

        public SensorStateFacts? SensorState { get; init; }

        public ScannerFacts? Scanner { get; init; }

        public DeviceErrorFacts? DeviceError { get; init; }
    }

    public sealed record TagReadFacts
    {
        public string? TagId { get; init; }

        public string? EpcRaw { get; init; }

        public long? RoutingFileId { get; init; }

        public string? ReadTimeText { get; init; }
    }

    public sealed record GateStateFacts
    {
        public int? StateCode { get; init; }

        public string? RawValue { get; init; }
    }

    public sealed record SignalFacts
    {
        public int? AntennaPort { get; init; }

        public DateTimeOffset? FirstSeenAtLocal { get; init; }

        public DateTimeOffset? LastSeenAtLocal { get; init; }

        public int? SeenCount { get; init; }

        public int? TxPower { get; init; }

        public double? DopplerFrequency { get; init; }

        public double? PhaseAngle { get; init; }

        public double? ChannelMhz { get; init; }

        public double? PeakRssiDbm { get; init; }
    }

    public sealed record BusinessEventFacts
    {
        public int? EventType { get; init; }

        public int? ProcessId { get; init; }

        public int? Quantity { get; init; }

        public string? ProcessIdsRaw { get; init; }

        public IReadOnlyList<int>? ProcessIds { get; init; }

        public int? Second { get; init; }
    }

    public sealed record StyleProcessFacts
    {
        public string? ProcessCustomRaw { get; init; }

        public IReadOnlyList<int>? ProcessCustom { get; init; }
    }

    public sealed record UserFacts
    {
        public int? UserId { get; init; }

        public string? UserName { get; init; }
    }

    public sealed record ConnectionFacts
    {
        public string? Status { get; init; }

        public string? Reason { get; init; }

        public bool? IsStart { get; init; }

        public bool? IsConnecting { get; init; }

        public bool? IsConnected { get; init; }

        public bool? IsSourceConnected { get; init; }

        public DateTimeOffset? ConnectedAtLocal { get; init; }
    }

    public sealed record DeviceOnlineFacts
    {
        public bool? Online { get; init; }

        public bool? Active { get; init; }

        public bool? IsSnapshot { get; init; }

        public string? SourceState { get; init; }

        public bool? IsStart { get; init; }

        public bool? IsUsed { get; init; }

        public bool? IsConnecting { get; init; }

        public bool? IsConnected { get; init; }

        public bool? IsGreenLighting { get; init; }

        public bool? IsRedLighting { get; init; }

        public string? GateState { get; init; }
    }

    public sealed record DeviceControlStateFacts
    {
        public string? Control { get; init; }

        public string? State { get; init; }

        public string? RawState { get; init; }
    }

    public sealed record SensorStateFacts
    {
        public string? Sensor { get; init; }

        public string? State { get; init; }

        public double? Timeout { get; init; }

        public string? TimeoutUnit { get; init; }
    }

    public sealed record ScannerFacts
    {
        public int? SessionType { get; init; }

        public int? DeviceType { get; init; }

        public string? ConnectionIdHash { get; init; }
    }

    public sealed record DeviceErrorFacts
    {
        public string? Code { get; init; }

        public string? Message { get; init; }

        public string? Severity { get; init; }

        public bool? Retryable { get; init; }
    }

    public sealed record ParseContext
    {
        public required string Status { get; init; }

        public required string ParserVersion { get; init; }

        public IReadOnlyList<string> Warnings { get; init; } = [];

        public IReadOnlyList<string> Errors { get; init; } = [];
    }

    public sealed record IngestionContext
    {
        public required string WorkerId { get; init; }

        public int Attempt { get; init; } = 1;

        public long? ProcessingDurationMs { get; init; }
    }
}
