using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class AppHubConnectionMetricMapper : IDeviceMetricMapper
{
    public IReadOnlyCollection<string> Keys { get; } =
    [
        MetricMapperKeyExtensions.CreateKey("erp_apphub", "device_connection", "receiveStateConnected", "connection"),
        MetricMapperKeyExtensions.CreateKey("erp_apphub", "scanner_connection", "receiveDeviceScanConnect", "connection"),
        MetricMapperKeyExtensions.CreateKey("erp_apphub", "scanner_connection", "receiveDeviceScanDisconnect", "connection")
    ];

    public IReadOnlyList<MetricContributionDraft> Map(HistoryEvent historyEvent, StatisticsBucket bucket)
    {
        var metricCode = historyEvent.Category == "device_connection"
            ? historyEvent.Facts.Connection?.Status switch
            {
                "connected" => "device_connected",
                "disconnected" => "device_disconnected",
                _ => null
            }
            : historyEvent.SourceEventName == "receiveDeviceScanConnect"
                ? "scanner_connected"
                : historyEvent.SourceEventName == "receiveDeviceScanDisconnect"
                    ? "scanner_disconnected"
                    : null;

        return metricCode is null
            ? []
            : [MetricContributionFactory.Create(historyEvent, bucket, metricCode)];
    }
}
