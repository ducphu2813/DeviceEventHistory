using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

public interface IRawLogSourceFileDiscovery
{
    RawLogSourceMode Mode { get; }

    Task<IReadOnlyList<RawLogFileDescriptor>> DiscoverAsync(
        AntennaSourceOptions source,
        DateOnly folderDate,
        CancellationToken cancellationToken);
}
