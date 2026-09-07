using DeviceEventStatistics.Domain.Projection;

namespace DeviceEventStatistics.Application.Projection;

public interface IProjectionScopeReader
{
    Task<IReadOnlyList<ProjectionDeviceKey>> ReadDeviceKeysAsync(
        ProjectionIdentity identity,
        IReadOnlyCollection<long> companyIds,
        IReadOnlyCollection<long> deviceIds,
        CancellationToken cancellationToken = default);
}
