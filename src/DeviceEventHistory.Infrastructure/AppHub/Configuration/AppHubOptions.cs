using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.AppHub.Configuration;

public sealed class AppHubOptions
{
    public const string SectionName = AppConst.Configuration.AppHubSection;

    public bool Enabled { get; set; }

    public List<AppHubSourceOptions> Sources { get; set; } = [];
}
