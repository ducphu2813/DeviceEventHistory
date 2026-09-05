using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Mapping;

public sealed class DeviceMetricMapperRegistry
{
    private readonly IReadOnlyDictionary<string, IDeviceMetricMapper> mappers;

    public DeviceMetricMapperRegistry(IEnumerable<IDeviceMetricMapper> metricMappers)
    {
        var registeredMappers = metricMappers.ToArray();
        var entries = new Dictionary<string, IDeviceMetricMapper>(StringComparer.Ordinal);
        foreach (var mapper in registeredMappers)
        {
            foreach (var key in mapper.Keys)
            {
                if (!entries.TryAdd(key, mapper))
                {
                    throw new InvalidOperationException(
                        StatisticsContractConstants.Messages.Format(
                            StatisticsContractConstants.Messages.MSG_METRIC_MAPPER_DUPLICATE,
                            key));
                }
            }
        }

        mappers = entries;
        RequiredMetricCodes = registeredMappers
            .SelectMany(mapper => mapper.MetricCodes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyCollection<string> RequiredMetricCodes { get; }

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
