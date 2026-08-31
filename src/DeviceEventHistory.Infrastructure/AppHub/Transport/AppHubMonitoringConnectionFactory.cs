using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json.Linq;

namespace DeviceEventHistory.Infrastructure.AppHub.Transport;

public sealed class AppHubMonitoringConnectionFactory : IAppHubMonitoringConnectionFactory
{
    public IAppHubMonitoringConnection Create(AppHubSourceOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var queryString = CreateCredentialQuery(source);
        var connection = new HubConnection(source.Endpoint.Trim(), queryString);
        var client = new SignalRClientAdapter(connection);
        return new AppHubMonitoringConnection(
            source.SourceId.Trim(),
            source.HubName.Trim(),
            client,
            Guid.NewGuid().ToString("N"));
    }

    private static IDictionary<string, string> CreateCredentialQuery(AppHubSourceOptions source)
    {
        var accessToken = GetConfiguredValue(
            source.AccessToken,
            source.AccessTokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["token"] = accessToken
            };
        }

        var jwtToken = GetConfiguredValue(
            source.TokenJwt,
            source.TokenJwtEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(jwtToken))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tokenjwt"] = jwtToken
            };
        }

        throw new InvalidOperationException(AppConst.Messages.MSG_APPHUB_CREDENTIAL_VALUE_REQUIRED);
    }

    private static string? GetConfiguredValue(
        string? directValue,
        string? environmentVariableName)
    {
        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue;
        }

        return string.IsNullOrWhiteSpace(environmentVariableName)
            ? null
            : Environment.GetEnvironmentVariable(environmentVariableName.Trim());
    }

    private sealed class SignalRClientAdapter(HubConnection connection) : IAppHubSignalRClient
    {
        public event Action? Reconnecting
        {
            add => connection.Reconnecting += value;
            remove => connection.Reconnecting -= value;
        }

        public event Action? Reconnected
        {
            add => connection.Reconnected += value;
            remove => connection.Reconnected -= value;
        }

        public event Action? Closed
        {
            add => connection.Closed += value;
            remove => connection.Closed -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken) =>
            connection.Start().WaitAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            connection.Stop();
            return Task.CompletedTask;
        }

        public IAppHubSignalRProxy CreateProxy(string hubName) =>
            new SignalRProxyAdapter(connection.CreateHubProxy(hubName));

        public void Dispose() => connection.Dispose();
    }

    private sealed class SignalRProxyAdapter(IHubProxy proxy) : IAppHubSignalRProxy
    {
        public IDisposable RegisterCallback(string eventName, Action<object[]> callback) =>
            proxy.Observe(eventName).Subscribe(new CallbackObserver(callback));

        public Task JoinMonitoringAsync(CancellationToken cancellationToken) =>
            proxy.Invoke(AppConst.AppHub.JoinMonitoringMethod).WaitAsync(cancellationToken);
    }

    private sealed class CallbackObserver(Action<object[]> callback) : IObserver<IList<JToken>>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(IList<JToken> value) => callback(value.Cast<object>().ToArray());
    }

}
