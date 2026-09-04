using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class DeviceMetricMapperRegistry
{
    private readonly IReadOnlyDictionary<string, IDeviceMetricMapper> mappers;

    public DeviceMetricMapperRegistry(IEnumerable<IDeviceMetricMapper> metricMappers)
    {
        var entries = new Dictionary<string, IDeviceMetricMapper>(StringComparer.Ordinal);
        foreach (var mapper in metricMappers)
        {
            foreach (var key in mapper.Keys)
            {
                if (!entries.TryAdd(key, mapper))
                {
                    throw new InvalidOperationException(
                        $"STAT-METRIC-MAPPER-DUPLICATE: Mapping key '{key}' is registered more than once.");
                }
            }
        }

        mappers = entries;
    }

    public bool TryMap(
        HistoryEvent historyEvent,
        StatisticsBucket bucket,
        out IReadOnlyList<MetricContributionDraft> contributions)
    {
        var factsDiscriminator = GetFactsDiscriminator(historyEvent);
        var key = MetricMapperKey.Create(historyEvent, factsDiscriminator);
        if (!mappers.TryGetValue(key, out var mapper))
        {
            contributions = [];
            return false;
        }

        contributions = mapper.Map(historyEvent, bucket);
        return contributions.Count > 0;
    }

    private static string GetFactsDiscriminator(HistoryEvent historyEvent) =>
        historyEvent.Facts switch
        {
            { TagRead: not null } => "tagRead",
            { BusinessEvent: not null } => "businessEvent",
            { Connection: not null } => "connection",
            { DeviceOnline: not null } => "deviceOnline",
            { DeviceControlState: not null } => "deviceControlState",
            { SensorState: not null } => "sensorState",
            { Scanner: not null } => "scanner",
            { DeviceError: not null } => "deviceError",
            _ => string.Empty
        };
}
