using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.Parsing;

public sealed record RawRecordProcessingResult
{
    public CanonicalDeviceEvent? Event { get; init; }

    public CanonicalIngestionFailure? Failure { get; init; }

    public sealed record CanonicalIngestionFailure
    {
        public required string FailureId { get; init; }

        public required string Code { get; init; }

        public required string Message { get; init; }

        public required string ParserVersion { get; init; }

        public required RawRecordContext Context { get; init; }

        public bool Retryable { get; init; }
    }
}
