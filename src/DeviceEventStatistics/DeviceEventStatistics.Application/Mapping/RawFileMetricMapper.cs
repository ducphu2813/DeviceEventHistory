using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class RawFileMetricMapper : IDeviceMetricMapper
{
    public IReadOnlyCollection<string> Keys { get; } =
    [
        MetricMapperKeyExtensions.CreateKey("rfid_antenna_file", "tag_read", "raw_record", "tagRead"),
        MetricMapperKeyExtensions.CreateKey("rfid_antenna_file", "business_event", "raw_record", "businessEvent")
    ];

    public IReadOnlyCollection<string> MetricCodes { get; } = ["tag_read", "business_process"];

    public IReadOnlyList<MetricContributionDraft> Map(HistoryEvent historyEvent, StatisticsBucket bucket)
    {
        var metricCode = historyEvent.Category == "tag_read" ? "tag_read" : "business_process";
        return [MetricContributionFactory.Create(historyEvent, bucket, metricCode)];
    }
}

internal static class MetricMapperKeyExtensions
{
    public static string CreateKey(
        string sourceKind,
        string category,
        string eventName,
        string factsDiscriminator) =>
        string.Join('|', sourceKind, category, eventName, factsDiscriminator);
}
