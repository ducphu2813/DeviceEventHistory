using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class AppHubDeviceOnlineMetricMapper : IDeviceMetricMapper
{
    public IReadOnlyCollection<string> Keys { get; } =
    [
        MetricMapperKeyExtensions.CreateKey(
            "erp_apphub",
            "device_online",
            "receiveDeviceOnline",
            "deviceOnline")
    ];

    public IReadOnlyList<MetricContributionDraft> Map(
        HistoryEvent historyEvent,
        StatisticsBucket bucket) =>
        historyEvent.Facts.DeviceOnline is null
            ? []
            : [MetricContributionFactory.Create(historyEvent, bucket, "device_online_observed")];
}
