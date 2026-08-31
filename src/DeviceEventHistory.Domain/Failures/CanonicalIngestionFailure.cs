using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Domain.Failures;

/// <summary>
/// Source-neutral representation of an event that could not be canonicalized
/// into normal history. Infrastructure failures are intentionally not modeled
/// here; they belong to retry and health handling.
/// </summary>
public sealed record CanonicalIngestionFailure
{
    public required string FailureId { get; init; }

    public required int SchemaVersion { get; init; }

    public required string SourceKind { get; init; }

    public int? CompanyId { get; init; }

    public required CanonicalDeviceEvent.SourceContext Source { get; init; }

    public required CanonicalDeviceEvent.RawPayloadContext RawPayload { get; init; }

    public required ErrorContext Error { get; init; }

    public DateTimeOffset? ReceivedAtUtc { get; init; }

    public DateTimeOffset? PersistedAtUtc { get; init; }

    public bool Retryable { get; init; }

    public int RetryCount { get; init; }

    public DateTimeOffset? ResolvedAtUtc { get; init; }

    public string? Resolution { get; init; }

    public CanonicalDeviceEvent.IngestionContext? Ingestion { get; init; }

    public sealed record ErrorContext
    {
        public required string Code { get; init; }

        public required string Message { get; init; }

        public required string Stage { get; init; }

        public required string ParserVersion { get; init; }

        public IReadOnlyList<string> Details { get; init; } = [];
    }
}
