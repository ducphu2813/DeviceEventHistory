using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Persistence;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class HistoryEventEligibilityPolicy
{
    private static readonly IReadOnlySet<int> SupportedSchemaVersions = new HashSet<int> { 1, 2 };
    private static readonly IReadOnlySet<string> SupportedTimeBases =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "occurred", "received" };

    public EligibilityDecision Evaluate(HistoryEvent historyEvent)
    {
        if (historyEvent.EventId is null || !IsLowercaseSha256(historyEvent.EventId))
        {
            return Failed("STAT_EVENT_ID_INVALID");
        }

        if (historyEvent.SchemaVersion is not int schemaVersion ||
            !SupportedSchemaVersions.Contains(schemaVersion))
        {
            return Failed("STAT_SCHEMA_UNSUPPORTED");
        }

        if (string.IsNullOrWhiteSpace(historyEvent.SourceKind) ||
            string.IsNullOrWhiteSpace(historyEvent.Category) ||
            string.IsNullOrWhiteSpace(historyEvent.SourceId))
        {
            return Failed("STAT_SOURCE_CONTRACT_INVALID");
        }

        if (historyEvent.PersistedAtUtc is not DateTimeOffset ||
            historyEvent.TimelineAtUtc is not DateTimeOffset)
        {
            return Failed("STAT_TIMELINE_REQUIRED");
        }

        if (historyEvent.TimeBasis is null || !SupportedTimeBases.Contains(historyEvent.TimeBasis))
        {
            return Failed("STAT_TIME_BASIS_INVALID");
        }

        if (string.Equals(historyEvent.ParseStatus, "unmapped", StringComparison.OrdinalIgnoreCase))
        {
            return new EligibilityDecision(ProjectionEventDisposition.QualityOnly, "STAT_EVENT_UNMAPPED");
        }

        if (!string.Equals(historyEvent.ParseStatus, "parsed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(historyEvent.ParseStatus, "parsed_with_warnings", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("STAT_PARSE_STATUS_UNSUPPORTED");
        }

        if (historyEvent.CompanyId is not > 0)
        {
            return Failed("STAT_TENANT_REQUIRED");
        }

        if (historyEvent.DeviceId is not > 0)
        {
            return Failed("STAT_DEVICE_REQUIRED");
        }

        return new EligibilityDecision(ProjectionEventDisposition.Aggregated, null);
    }

    private static EligibilityDecision Failed(string reasonCode) =>
        new(ProjectionEventDisposition.FailedTerminal, reasonCode);

    private static bool IsLowercaseSha256(string value) =>
        value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record EligibilityDecision(
    ProjectionEventDisposition Disposition,
    string? ReasonCode);
