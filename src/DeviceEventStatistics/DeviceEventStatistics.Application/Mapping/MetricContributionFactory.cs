using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

internal static class MetricContributionFactory
{
    public static MetricContributionDraft Create(
        HistoryEvent historyEvent,
        StatisticsBucket bucket,
        string metricCode) =>
        new(
            historyEvent.EventId!,
            historyEvent.CompanyId!.Value,
            historyEvent.DeviceId!.Value,
            bucket.StatisticsDate,
            metricCode,
            historyEvent.SourceKind!,
            historyEvent.TimelineAtUtc!.Value,
            historyEvent.PersistedAtUtc!.Value,
            string.Equals(historyEvent.ParseStatus, "parsed_with_warnings", StringComparison.OrdinalIgnoreCase),
            string.Equals(historyEvent.TimeBasis, "occurred", StringComparison.OrdinalIgnoreCase)
                ? EventTimeBasis.Occurred
                : EventTimeBasis.Received);
}
