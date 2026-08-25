using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

public sealed class RawLogFileDiscovery(
    RfidRawLogOptions options,
    IEnumerable<IRawLogSourceFileDiscovery> sourceDiscoveries,
    TimeProvider timeProvider) : IRawLogFileDiscovery
{
    private readonly IReadOnlyDictionary<RawLogSourceMode, IRawLogSourceFileDiscovery> sourceDiscoveries =
        sourceDiscoveries.ToDictionary(discovery => discovery.Mode);

    public async Task<IReadOnlyList<RawLogFileDescriptor>> DiscoverAsync(
        AntennaSourceOptions source,
        CancellationToken cancellationToken = default)
    {
        if (!source.Enabled)
        {
            return [];
        }

        if (!sourceDiscoveries.TryGetValue(source.Mode, out var sourceDiscovery))
        {
            throw new InvalidOperationException(
                AppConst.Messages.Format(AppConst.Messages.MSG_NO_RAW_LOG_DISCOVERY_ADAPTER, source.Mode));
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(source.TimeZoneId.Trim());
        var sourceToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
        var descriptors = new List<RawLogFileDescriptor>();

        for (var daysAgo = options.LookbackDays; daysAgo >= 0; daysAgo--)
        {
            var folderDate = sourceToday.AddDays(-daysAgo);
            descriptors.AddRange(await sourceDiscovery.DiscoverAsync(source, folderDate, cancellationToken));
        }

        return descriptors
            .OrderBy(descriptor => descriptor.FolderDate)
            .ThenBy(descriptor => descriptor.FileId)
            .ToArray();
    }
}
