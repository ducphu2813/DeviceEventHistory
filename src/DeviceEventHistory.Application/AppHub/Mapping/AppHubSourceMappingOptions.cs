using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed record AppHubSourceMappingOptions(
    string SourceId,
    int? CompanyId,
    bool DedicatedSingleTenant);

public interface IAppHubSourceConfigurationProvider
{
    bool TryGet(string sourceId, out AppHubSourceMappingOptions options);
}

public sealed class AppHubSourceConfigurationProvider
    : IAppHubSourceConfigurationProvider
{
    private readonly IReadOnlyDictionary<string, AppHubSourceMappingOptions> options;

    public AppHubSourceConfigurationProvider(
        IEnumerable<AppHubSourceMappingOptions> sourceOptions)
    {
        ArgumentNullException.ThrowIfNull(sourceOptions);

        var map = new Dictionary<string, AppHubSourceMappingOptions>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var option in sourceOptions)
        {
            ArgumentNullException.ThrowIfNull(option);
            if (string.IsNullOrWhiteSpace(option.SourceId))
            {
                throw new ArgumentException(
                    AppConst.Messages.MSG_APPHUB_SOURCE_MAPPING_ID_REQUIRED,
                    nameof(sourceOptions));
            }

            if (!map.TryAdd(option.SourceId.Trim(), option with
                {
                    SourceId = option.SourceId.Trim()
                }))
            {
                throw new InvalidOperationException(
                    AppConst.Messages.Format(
                        AppConst.Messages.MSG_APPHUB_SOURCE_MAPPING_ID_DUPLICATED,
                        option.SourceId));
            }
        }

        options = map;
    }

    public bool TryGet(string sourceId, out AppHubSourceMappingOptions options) =>
        this.options.TryGetValue(sourceId, out options!);
}
