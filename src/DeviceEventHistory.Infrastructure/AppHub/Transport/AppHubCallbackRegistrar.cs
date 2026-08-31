using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.AppHub.Transport;

/// <summary>
/// Registers the configured callback set before the SignalR connection starts.
/// The callback itself only forwards captured arguments to the admission layer.
/// </summary>
public sealed class AppHubCallbackRegistrar
{
    public IReadOnlyList<IDisposable> Register(
        IAppHubMonitoringConnection connection,
        IEnumerable<string> eventNames,
        Action<string, object[]> callback)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventNames);
        ArgumentNullException.ThrowIfNull(callback);

        var subscriptions = new List<IDisposable>();
        foreach (var eventName in eventNames)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                throw new ArgumentException(
                    AppConst.Messages.MSG_APPHUB_CALLBACK_NAME_REQUIRED,
                    nameof(eventNames));
            }

            var normalizedEventName = eventName.Trim();
            subscriptions.Add(connection.RegisterCallback(
                normalizedEventName,
                arguments => callback(normalizedEventName, arguments)));
        }

        return subscriptions;
    }
}
