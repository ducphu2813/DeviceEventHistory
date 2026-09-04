using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class AppHubScannerMetricMapper : IDeviceMetricMapper
{
    public IReadOnlyCollection<string> Keys { get; } =
    [MetricMapperKeyExtensions.CreateKey("erp_apphub", "device_snapshot", "receiveRequestDeviceScanInfoOnline", "connection")];

    public IReadOnlyList<MetricContributionDraft> Map(HistoryEvent historyEvent, StatisticsBucket bucket) =>
        historyEvent.Facts.Connection is null
            ? []
            : [MetricContributionFactory.Create(historyEvent, bucket, "snapshot_observed")];
}
