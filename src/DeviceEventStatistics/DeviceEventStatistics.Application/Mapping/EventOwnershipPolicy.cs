using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Persistence;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class EventOwnershipPolicy
{
    public const string Version = "v1";

    public OwnershipDecision Evaluate(HistoryEvent historyEvent)
    {
        var sourceKind = historyEvent.SourceKind ?? string.Empty;
        var category = historyEvent.Category ?? string.Empty;

        if (sourceKind.Equals("rfid_antenna_file", StringComparison.Ordinal) &&
            category is "tag_read" or "business_event")
        {
            return new OwnershipDecision(ProjectionEventDisposition.Aggregated, "primary");
        }

        if (sourceKind.Equals("erp_apphub", StringComparison.Ordinal) &&
            category == "tag_read")
        {
            return new OwnershipDecision(ProjectionEventDisposition.Ignored, "secondary_tag_source");
        }

        if (sourceKind.Equals("erp_apphub", StringComparison.Ordinal) &&
            category is "device_connection" or "device_control_state" or
            "device_sensor_state" or "scanner_connection" or "device_online" or
            "device_snapshot")
        {
            return new OwnershipDecision(ProjectionEventDisposition.Aggregated, "primary");
        }

        return new OwnershipDecision(ProjectionEventDisposition.QualityOnly, "source_not_owned");
    }
}

public sealed record OwnershipDecision(
    ProjectionEventDisposition Disposition,
    string ReasonCode);
