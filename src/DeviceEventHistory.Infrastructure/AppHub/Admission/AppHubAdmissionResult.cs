using DeviceEventHistory.Application.Ingestion;

namespace DeviceEventHistory.Infrastructure.AppHub.Admission;

public sealed record AppHubAdmissionResult(
    bool IsAdmitted,
    RawSourceEvent? SourceEvent,
    string? DropReason)
{
    public static AppHubAdmissionResult Admitted(RawSourceEvent sourceEvent) =>
        new(true, sourceEvent, null);

    public static AppHubAdmissionResult Dropped(string reason) =>
        new(false, null, reason);
}
