using DeviceEventStatistics.Domain.Projection;

namespace DeviceEventStatistics.Application.Projection;

public static class ProjectionScopeSelector
{
    public static IReadOnlyList<ProjectionDeviceKey> Select(
        IEnumerable<ProjectionDeviceKey> candidates,
        IReadOnlyCollection<long> companyIds,
        IReadOnlyCollection<long> deviceIds)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(companyIds);
        ArgumentNullException.ThrowIfNull(deviceIds);

        var companyFilter = companyIds.Count == 0
            ? null
            : companyIds.ToHashSet();
        var deviceFilter = deviceIds.Count == 0
            ? null
            : deviceIds.ToHashSet();

        return candidates
            .Where(key => companyFilter is null || companyFilter.Contains(key.CompanyId))
            .Where(key => deviceFilter is null || deviceFilter.Contains(key.DeviceId))
            .Distinct()
            .OrderBy(key => key.CompanyId)
            .ThenBy(key => key.DeviceId)
            .ToArray();
    }
}
