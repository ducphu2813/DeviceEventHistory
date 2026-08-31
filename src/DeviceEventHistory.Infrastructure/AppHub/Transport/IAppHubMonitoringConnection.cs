namespace DeviceEventHistory.Infrastructure.AppHub.Transport;

public interface IAppHubMonitoringConnection : IAsyncDisposable
{
    string SourceId { get; }

    string ConnectionGeneration { get; }

    AppHubConnectionState State { get; }

    event Action<AppHubConnectionState>? StateChanged;

    event Action<Exception>? LifecycleFailed;

    IDisposable RegisterCallback(string eventName, Action<object[]> callback);

    Task StartAsync(CancellationToken cancellationToken);

    Task JoinMonitoringAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
