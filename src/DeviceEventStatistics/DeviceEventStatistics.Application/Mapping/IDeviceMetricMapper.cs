using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public interface IDeviceMetricMapper
{
    IReadOnlyCollection<string> Keys { get; }

    IReadOnlyList<MetricContributionDraft> Map(
        HistoryEvent historyEvent,
        StatisticsBucket bucket);
}

public static class MetricMapperKey
{
    public static string Create(HistoryEvent historyEvent, string factsDiscriminator) =>
        string.Join('|',
            historyEvent.SourceKind ?? string.Empty,
            historyEvent.Category ?? string.Empty,
            historyEvent.SourceEventName ?? string.Empty,
            factsDiscriminator);
}
