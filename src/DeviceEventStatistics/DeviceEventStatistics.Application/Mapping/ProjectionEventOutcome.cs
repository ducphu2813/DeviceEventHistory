using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed record ProjectionEventOutcome(
    History.HistoryEvent Event,
    ProjectionEventDisposition Disposition,
    string? ReasonCode,
    IReadOnlyList<MetricContributionDraft> Metrics,
    IReadOnlyList<QualityContributionDraft> Quality,
    IReadOnlyList<string> Diagnostics,
    ProjectionFailureInput? Failure = null)
{
    public static ProjectionEventOutcome QualityOnly(
        History.HistoryEvent historyEvent,
        string reasonCode,
        IReadOnlyList<QualityContributionDraft> quality,
        IReadOnlyList<string>? diagnostics = null) =>
        new(historyEvent, ProjectionEventDisposition.QualityOnly, reasonCode, [], quality, diagnostics ?? []);
}

public sealed record MetricContributionDraft(
    string EventId,
    long CompanyId,
    long DeviceId,
    DateOnly StatisticsDate,
    string MetricCode,
    string SourceKind,
    DateTimeOffset TimelineAtUtc,
    DateTimeOffset SourcePersistedAtUtc,
    bool ParsedWithWarnings,
    EventTimeBasis TimeBasis);

public sealed record QualityContributionDraft(
    string EventId,
    string QualityIdentity,
    DateOnly StatisticsDate,
    long CompanyId,
    string SourceKind,
    string SourceId,
    string QualityCode,
    DateTimeOffset SeenAtUtc);
