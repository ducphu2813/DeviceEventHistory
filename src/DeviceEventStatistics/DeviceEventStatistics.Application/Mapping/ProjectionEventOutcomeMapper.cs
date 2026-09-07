using System.Security.Cryptography;
using System.Text;
using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class ProjectionEventOutcomeMapper(
    HistoryEventEligibilityPolicy eligibilityPolicy,
    EventOwnershipPolicy ownershipPolicy,
    DeviceMetricMapperRegistry metricRegistry,
    LocalStatisticsDateResolver dateResolver)
{
    public ProjectionEventOutcome Map(HistoryEvent historyEvent)
    {
        var eligibility = eligibilityPolicy.Evaluate(historyEvent);
        if (eligibility.Disposition is ProjectionEventDisposition.FailedTerminal)
        {
            return Failed(historyEvent, eligibility.ReasonCode!);
        }

        var bucket = dateResolver.Resolve(historyEvent.TimelineAtUtc!.Value);
        if (eligibility.Disposition is ProjectionEventDisposition.QualityOnly)
        {
            return QualityOnly(historyEvent, eligibility.ReasonCode!, bucket.StatisticsDate);
        }

        var ownership = ownershipPolicy.Evaluate(historyEvent);
        if (ownership.Disposition is ProjectionEventDisposition.Ignored)
        {
            return new ProjectionEventOutcome(
                historyEvent,
                ProjectionEventDisposition.Ignored,
                ownership.ReasonCode,
                [],
                CreateQuality(historyEvent, bucket.StatisticsDate, "secondary_source_ignored"),
                historyEvent.MappingDiagnostics);
        }

        if (ownership.Disposition is ProjectionEventDisposition.QualityOnly)
        {
            return QualityOnly(historyEvent, ownership.ReasonCode, bucket.StatisticsDate);
        }

        if (!metricRegistry.TryMap(historyEvent, bucket, out var metrics))
        {
            return QualityOnly(historyEvent, "STAT_METRIC_UNMAPPED", bucket.StatisticsDate);
        }

        return new ProjectionEventOutcome(
            historyEvent,
            ProjectionEventDisposition.Aggregated,
            null,
            metrics,
            CreateQualityForWarnings(historyEvent, bucket.StatisticsDate),
            historyEvent.MappingDiagnostics);
    }

    private static ProjectionEventOutcome Failed(HistoryEvent historyEvent, string reasonCode)
    {
        var failureId = CreateFailureId(historyEvent, reasonCode);
        var now = historyEvent.PersistedAtUtc ??
                  historyEvent.ReceivedAtUtc ??
                  historyEvent.TimelineAtUtc ??
                  DateTimeOffset.UnixEpoch;
        return new ProjectionEventOutcome(
            historyEvent,
            ProjectionEventDisposition.FailedTerminal,
            reasonCode,
            [],
            [],
            historyEvent.MappingDiagnostics,
            new ProjectionFailureInput(
                failureId,
                historyEvent.SourceDocumentId,
                reasonCode,
                "eligibility",
                reasonCode,
                false,
                0,
                now,
                now,
                historyEvent.EventId,
                historyEvent.CompanyId,
                historyEvent.DeviceId,
                historyEvent.SourceKind,
                historyEvent.Category,
                historyEvent.SourceEventName,
                historyEvent.PersistedAtUtc));
    }

    private static ProjectionEventOutcome QualityOnly(
        HistoryEvent historyEvent,
        string reasonCode,
        DateOnly statisticsDate) =>
        new(
            historyEvent,
            ProjectionEventDisposition.QualityOnly,
            reasonCode,
            [],
            CreateQuality(historyEvent, statisticsDate, QualityCode(reasonCode)),
            historyEvent.MappingDiagnostics);

    private static IReadOnlyList<QualityContributionDraft> CreateQualityForWarnings(
        HistoryEvent historyEvent,
        DateOnly statisticsDate)
    {
        var quality = new List<QualityContributionDraft>();
        if (string.Equals(historyEvent.ParseStatus, "parsed_with_warnings", StringComparison.OrdinalIgnoreCase))
        {
            quality.AddRange(CreateQuality(historyEvent, statisticsDate, "parsed_with_warnings"));
        }

        if (string.Equals(historyEvent.TimeBasis, "received", StringComparison.OrdinalIgnoreCase))
        {
            quality.AddRange(CreateQuality(historyEvent, statisticsDate, "received_time_basis"));
        }

        return quality;
    }

    private static IReadOnlyList<QualityContributionDraft> CreateQuality(
        HistoryEvent historyEvent,
        DateOnly statisticsDate,
        string qualityCode)
    {
        if (historyEvent.PersistedAtUtc is not DateTimeOffset seenAtUtc ||
            string.IsNullOrWhiteSpace(historyEvent.SourceKind) ||
            string.IsNullOrWhiteSpace(historyEvent.SourceId))
        {
            return [];
        }

        var eventId = historyEvent.EventId ?? string.Empty;
        return
        [
            new QualityContributionDraft(
                eventId,
                $"{historyEvent.SourceDocumentId}|{qualityCode}",
                statisticsDate,
                historyEvent.CompanyId is > 0 ? historyEvent.CompanyId.Value : 0,
                historyEvent.SourceKind,
                historyEvent.SourceId,
                qualityCode,
                seenAtUtc)
        ];
    }

    private static string QualityCode(string reasonCode) => reasonCode switch
    {
        "STAT_SCHEMA_UNSUPPORTED" => "unsupported_schema",
        "STAT_EVENT_UNMAPPED" or "STAT_METRIC_UNMAPPED" => "unmapped",
        _ => "projection_failure"
    };

    private static string CreateFailureId(HistoryEvent historyEvent, string reasonCode)
    {
        var input = Encoding.UTF8.GetBytes(
            "device_event_daily|1|" +
            historyEvent.SourceDocumentId +
            "|" +
            historyEvent.EventId +
            "|" +
            reasonCode);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
