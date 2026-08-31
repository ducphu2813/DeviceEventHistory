using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.Application.Parsing;

public sealed record RawRecordProcessingResult
{
    public CanonicalDeviceEvent? Event { get; init; }

    public CanonicalIngestionFailure? Failure { get; init; }

    public RawRecordParseStatus? ParseStatus { get; init; }
}
