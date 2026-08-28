using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.Ingestion;

/// <summary>
/// Result of mapping one admitted source event. Exactly one outcome is valid.
/// </summary>
public sealed record CanonicalIngestionResult
{
    public CanonicalDeviceEvent? Event { get; init; }

    public CanonicalIngestionFailure? Failure { get; init; }

    public bool HasExactlyOneOutcome => (Event is null) != (Failure is null);

    public string Identity => Event?.EventId ?? Failure?.FailureId ?? string.Empty;

    public static CanonicalIngestionResult FromEvent(CanonicalDeviceEvent deviceEvent) => new()
    {
        Event = deviceEvent
    };

    public static CanonicalIngestionResult FromFailure(CanonicalIngestionFailure failure) => new()
    {
        Failure = failure
    };

    public void EnsureExactlyOneOutcome()
    {
        if (Event is null && Failure is null)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_CANONICAL_INGESTION_OUTCOME_REQUIRED);
        }

        if (Event is not null && Failure is not null)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_CANONICAL_INGESTION_OUTCOME_EXCLUSIVE);
        }
    }
}
