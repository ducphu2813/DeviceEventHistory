using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

namespace DeviceEventHistory.Infrastructure.Metadata;

public sealed class ConfigurationDeviceMetadataResolver : IDeviceMetadataResolver
{
    private readonly IReadOnlyDictionary<string, SourceDefinition> sources;

    public ConfigurationDeviceMetadataResolver(RfidRawLogOptions options)
    {
        sources = options.Sources
            .Select(source => new SourceDefinition
            {
                SourceId = source.SourceId.Trim(),
                CompanyId = source.CompanyId,
                RootPath = source.RootPath.Trim(),
                TimeZoneId = source.TimeZoneId.Trim(),
                FilePattern = source.FilePattern.Trim(),
                Enabled = source.Enabled
            })
            .ToDictionary(source => source.SourceId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<SourceDefinition> GetSources() => sources.Values.ToArray();

    public bool TryGetSource(string sourceId, out SourceDefinition? source)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            source = null;
            return false;
        }

        return sources.TryGetValue(sourceId.Trim(), out source);
    }
}
