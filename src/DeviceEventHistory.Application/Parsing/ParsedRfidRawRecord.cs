namespace DeviceEventHistory.Application.Parsing;

public sealed record ParsedRfidRawRecord
{
    public RfidHeaderFacts? Header { get; init; }

    public GateStateFacts? GateState { get; init; }

    public SignalFacts? Signal { get; init; }

    public BusinessEventFacts? BusinessEvent { get; init; }

    public StyleProcessFacts? StyleProcess { get; init; }

    public UserFacts? User { get; init; }

    public sealed record RfidHeaderFacts
    {
        public string? TagId { get; init; }

        public string? ReadTimeText { get; init; }

        public TimeSpan? ReadTime { get; init; }

        public int? DeviceId { get; init; }

        public int? GateId { get; init; }
    }

    public sealed record GateStateFacts
    {
        public int? StateCode { get; init; }

        public string? RawValue { get; init; }
    }

    public sealed record SignalFacts
    {
        public int? AntennaPort { get; init; }

        public DateTime? FirstSeenAtLocal { get; init; }

        public DateTime? LastSeenAtLocal { get; init; }

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
}
