using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.Application.Mapping;

public interface IDeviceMetricMapper
{
    IReadOnlyCollection<string> Keys { get; }

    IReadOnlyCollection<string> MetricCodes { get; }

    IReadOnlyList<MetricContributionDraft> Map(
        HistoryEvent historyEvent,
        StatisticsBucket bucket);
}

public interface IMetricKeyResolver
{
    Task<IReadOnlyDictionary<string, int>> ResolveAsync(
        int metricSetVersion,
        IReadOnlyCollection<string> metricCodes,
        CancellationToken cancellationToken = default);

    async Task<ResolvedMetricRegistry> ResolveRegistryAsync(
        MetricRegistryIdentity identity,
        IReadOnlyCollection<string> metricCodes,
        CancellationToken cancellationToken = default)
    {
        var metricKeys = await ResolveAsync(
            identity.MetricSetVersion,
            metricCodes,
            cancellationToken);
        return new(identity, metricKeys);
    }
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
