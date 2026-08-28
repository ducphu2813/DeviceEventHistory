using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.AppHub.Configuration;

public sealed class AppHubSourceOptions
{
    public string SourceId { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string HubName { get; set; } = AppConst.AppHub.DefaultHubName;

    public int? CompanyId { get; set; }

    public bool DedicatedSingleTenant { get; set; }

    public int ChannelCapacity { get; set; } = AppConst.Defaults.AppHubChannelCapacity;

    public TimeSpan EnqueueTimeout { get; set; } =
        TimeSpan.FromMilliseconds(AppConst.Defaults.AppHubEnqueueTimeoutMilliseconds);

    public TimeSpan ReconnectMinDelay { get; set; } =
        TimeSpan.FromSeconds(AppConst.Defaults.AppHubReconnectMinDelaySeconds);

    public TimeSpan ReconnectMaxDelay { get; set; } =
        TimeSpan.FromSeconds(AppConst.Defaults.AppHubReconnectMaxDelaySeconds);

    public List<string> EnabledEvents { get; set; } = [];

    /// <summary>
    /// Name of the environment variable containing the approved UserCookie token.
    /// The token value is never part of configuration objects or logs.
    /// </summary>
    public string? AccessTokenEnvironmentVariable { get; set; }

    /// <summary>
    /// Name of the environment variable containing the approved JWT token.
    /// Used as the SignalR <c>tokenjwt</c> query value when no UserCookie token exists.
    /// </summary>
    public string? TokenJwtEnvironmentVariable { get; set; }
}
