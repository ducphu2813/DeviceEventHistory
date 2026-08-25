using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

public interface IRawLogFileDiscovery
{
    Task<IReadOnlyList<RawLogFileDescriptor>> DiscoverAsync(
        AntennaSourceOptions source,
        CancellationToken cancellationToken = default);
}
