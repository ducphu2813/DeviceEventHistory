using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class AppHubSensorMetricMapper : IDeviceMetricMapper
{
    public IReadOnlyCollection<string> Keys { get; } =
    [MetricMapperKeyExtensions.CreateKey("erp_apphub", "device_sensor_state", "receiveTimeSensor", "sensorState")];

    public IReadOnlyCollection<string> MetricCodes { get; } = ["sensor_state_observed"];

    public IReadOnlyList<MetricContributionDraft> Map(HistoryEvent historyEvent, StatisticsBucket bucket) =>
        historyEvent.Facts.SensorState is null
            ? []
            : [MetricContributionFactory.Create(historyEvent, bucket, "sensor_state_observed")];
}
