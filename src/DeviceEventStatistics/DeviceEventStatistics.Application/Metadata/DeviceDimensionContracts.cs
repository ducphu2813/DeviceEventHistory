namespace DeviceEventStatistics.Application.Metadata;

public interface IDeviceDimensionStore
{
    Task UpsertAsync(
        IReadOnlyCollection<DeviceMetadata> metadata,
        CancellationToken cancellationToken = default);
}
