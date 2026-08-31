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
    /// Approved UserCookie token for local/development configuration.
    /// Production deployments should prefer a secret provider or environment variable.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Approved UserCookie token environment variable name.
    /// The token value is never part of logs or telemetry.
    /// </summary>
    public string? AccessTokenEnvironmentVariable { get; set; }

    /// <summary>
    /// Approved JWT token for local/development configuration.
    /// Used as the SignalR <c>tokenjwt</c> query value when no UserCookie token exists.
    /// </summary>
    public string? TokenJwt { get; set; }

    /// <summary>
    /// Approved JWT token environment variable name.
    /// Used as the SignalR <c>tokenjwt</c> query value when no UserCookie token exists.
    /// </summary>
    public string? TokenJwtEnvironmentVariable { get; set; }
}
