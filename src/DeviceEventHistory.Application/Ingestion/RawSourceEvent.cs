namespace DeviceEventHistory.Application.Ingestion;

/// <summary>
/// Immutable source-neutral envelope created at a source admission boundary.
/// </summary>
public sealed record RawSourceEvent
{
    public required string IngestionEventId { get; init; }

    public required string SourceKind { get; init; }

    public required string SourceId { get; init; }

    public required string SourceApplication { get; init; }

    public required string SourceTransport { get; init; }

    public required string EventName { get; init; }

    public required DateTimeOffset ReceivedAtUtc { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public required string RawArgumentsJson { get; init; }

    public required string PayloadSha256 { get; init; }

    public required string ConnectionGeneration { get; init; }

    public required long ReceiveSequence { get; init; }

    public required string DeliveryKind { get; init; }
}
