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

    public required SourceContext Source { get; init; }

    public DeviceContext? Device { get; init; }

    public required RawPayloadContext RawPayload { get; init; }

    public required FactsContext Facts { get; init; }

    public required ParseContext Parse { get; init; }

    public sealed record SourceContext
    {
        public required string Producer { get; init; }

        public required string SourceId { get; init; }

        public required long FileId { get; init; }

        public required string FileName { get; init; }

        public required string RelativePath { get; init; }

        public required DateOnly FolderDate { get; init; }

        public required long OffsetStart { get; init; }

        public required long OffsetEnd { get; init; }
    }

    public sealed record DeviceContext
    {
        public int? Id { get; init; }

        public int? GateId { get; init; }
    }

    public sealed record RawPayloadContext
    {
        public required string Format { get; init; }

        public required string Text { get; init; }

        public required string Sha256 { get; init; }
    }

    public sealed record FactsContext
    {
        public TagReadFacts? TagRead { get; init; }

        public GateStateFacts? GateState { get; init; }

        public SignalFacts? Signal { get; init; }

        public BusinessEventFacts? BusinessEvent { get; init; }

        public StyleProcessFacts? StyleProcess { get; init; }

        public UserFacts? User { get; init; }
    }

    public sealed record TagReadFacts
    {
        public required string TagId { get; init; }

        public required long RoutingFileId { get; init; }
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
    }

    public sealed record ParseContext
    {
        public required string Status { get; init; }

        public required string ParserVersion { get; init; }

        public IReadOnlyList<string> Warnings { get; init; } = [];

        public IReadOnlyList<string> Errors { get; init; } = [];
    }
}
