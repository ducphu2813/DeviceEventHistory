using DeviceEventStatistics.Application.History;

namespace DeviceEventStatistics.Application.Metadata;

public static class DeviceMetadataBatchResolver
{
    public static IReadOnlyList<DeviceMetadata> Resolve(
        IEnumerable<HistoryEvent> events,
        IDeviceMetadataResolver metadataResolver)
    {
        return events
            .Where(value => value.CompanyId is > 0 && value.DeviceId is > 0)
            .GroupBy(value => (value.CompanyId!.Value, value.DeviceId!.Value))
            .Select(group => metadataResolver.Resolve(group.Last()))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }
}
