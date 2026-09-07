namespace DeviceEventStatistics.Application.History;

public sealed record HistoryEvent
{
    public string SourceDocumentId { get; init; } = string.Empty;

    public string? EventId { get; init; }

    public int? SchemaVersion { get; init; }

    public long? CompanyId { get; init; }

    public string? Category { get; init; }

    public string? SourceKind { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public DateTimeOffset? ReceivedAtUtc { get; init; }

    public DateTimeOffset? PersistedAtUtc { get; init; }

    public DateTimeOffset? TimelineAtUtc { get; init; }

    public string? TimeBasis { get; init; }

    public string? SourceId { get; init; }

    public string? SourceEventName { get; init; }

    public string? DeliveryKind { get; init; }

    public long? DeviceId { get; init; }

    public long? GateId { get; init; }

    public string? DeviceType { get; init; }

    public string? DeviceCode { get; init; }

    public string? DeviceName { get; init; }

    public string? GateCode { get; init; }

    public string? GateName { get; init; }

    public string? ParseStatus { get; init; }

    public IReadOnlyList<string> MappingDiagnostics { get; init; } = [];

    public HistoryFacts Facts { get; init; } = new();
}

public sealed record HistoryFacts
{
    public TagReadFacts? TagRead { get; init; }

    public BusinessEventFacts? BusinessEvent { get; init; }

    public ConnectionFacts? Connection { get; init; }

    public DeviceOnlineFacts? DeviceOnline { get; init; }

    public DeviceControlStateFacts? DeviceControlState { get; init; }

    public SensorStateFacts? SensorState { get; init; }

    public ScannerFacts? Scanner { get; init; }

    public DeviceErrorFacts? DeviceError { get; init; }
}

public sealed record TagReadFacts(string? TagId, string? EpcRaw, long? RoutingFileId);

public sealed record BusinessEventFacts(int? EventType, int? ProcessId, int? Quantity);

public sealed record ConnectionFacts(
    string? Status,
    bool? IsConnecting,
    bool? IsConnected,
    bool? IsSourceConnected);

public sealed record DeviceOnlineFacts(bool? Online, bool? Active, bool? IsSnapshot);

public sealed record DeviceControlStateFacts(string? Control, string? State, string? RawState);

public sealed record SensorStateFacts(string? Sensor, string? State, double? Timeout, string? TimeoutUnit);

public sealed record ScannerFacts(int? SessionType, int? DeviceType);

public sealed record DeviceErrorFacts(string? Code, string? Severity, bool? Retryable);
