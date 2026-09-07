using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class AppHubControlMetricMapper : IDeviceMetricMapper
{
    public IReadOnlyCollection<string> Keys { get; } =
    [
        MetricMapperKeyExtensions.CreateKey("erp_apphub", "device_control_state", "receiveGreenState", "deviceControlState"),
        MetricMapperKeyExtensions.CreateKey("erp_apphub", "device_control_state", "receiveRedState", "deviceControlState")
    ];

    public IReadOnlyCollection<string> MetricCodes { get; } =
        ["green_light_on", "green_light_off", "red_light_on", "red_light_off"];

    public IReadOnlyList<MetricContributionDraft> Map(HistoryEvent historyEvent, StatisticsBucket bucket)
    {
        var facts = historyEvent.Facts.DeviceControlState;
        var metricCode = facts?.Control switch
        {
            "green_light" when string.Equals(facts.State, "on", StringComparison.OrdinalIgnoreCase) => "green_light_on",
            "green_light" when string.Equals(facts.State, "off", StringComparison.OrdinalIgnoreCase) => "green_light_off",
            "red_light" when string.Equals(facts.State, "on", StringComparison.OrdinalIgnoreCase) => "red_light_on",
            "red_light" when string.Equals(facts.State, "off", StringComparison.OrdinalIgnoreCase) => "red_light_off",
            _ => null
        };

        return metricCode is null
            ? []
            : [MetricContributionFactory.Create(historyEvent, bucket, metricCode)];
    }
}
