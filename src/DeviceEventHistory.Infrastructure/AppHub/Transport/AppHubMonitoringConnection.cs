using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.AppHub.Transport;

public sealed class AppHubMonitoringConnection : IAppHubMonitoringConnection
{
    private readonly IAppHubSignalRClient client;
    private readonly IAppHubSignalRProxy proxy;
    private readonly List<IDisposable> subscriptions = [];
    private bool disposed;

    public AppHubMonitoringConnection(
        string sourceId,
        string hubName,
        IAppHubSignalRClient client,
        string connectionGeneration,
        bool subscribeToLifecycle = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hubName);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionGeneration);

        SourceId = sourceId;
        ConnectionGeneration = connectionGeneration;
        this.client = client;
        proxy = client.CreateProxy(hubName);

        if (subscribeToLifecycle)
        {
            client.Reconnecting += OnReconnecting;
            client.Reconnected += OnReconnected;
            client.Closed += OnClosed;
        }
    }

    public string SourceId { get; }

    public string ConnectionGeneration { get; }

    public AppHubConnectionState State { get; private set; } = AppHubConnectionState.Disconnected;

    public event Action<AppHubConnectionState>? StateChanged;

    public event Action<Exception>? LifecycleFailed;

    public IDisposable RegisterCallback(string eventName, Action<object[]> callback)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(callback);

        if (State != AppHubConnectionState.Disconnected)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_APPHUB_CALLBACK_REGISTERED_AFTER_START);
        }

        var subscription = proxy.RegisterCallback(eventName, callback);
        subscriptions.Add(subscription);
        return subscription;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (State != AppHubConnectionState.Disconnected)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_APPHUB_CONNECTION_ALREADY_STARTED);
        }

        SetState(AppHubConnectionState.Connecting);
        try
        {
            await client.StartAsync(cancellationToken);
            SetState(AppHubConnectionState.Connected);
            await JoinMonitoringAsync(cancellationToken);
        }
        catch
        {
            SetState(AppHubConnectionState.Disconnected);
            throw;
        }
    }

    public async Task JoinMonitoringAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (State != AppHubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_APPHUB_PROXY_REQUIRED);
        }

        SetState(AppHubConnectionState.JoiningMonitoring);
        try
        {
            await proxy.JoinMonitoringAsync(cancellationToken);
            SetState(AppHubConnectionState.Running);
        }
        catch
        {
            SetState(AppHubConnectionState.Connected);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SetState(AppHubConnectionState.Stopping);
        await client.StopAsync(cancellationToken);
        SetState(AppHubConnectionState.Disconnected);
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        subscriptions.Clear();
        client.Reconnecting -= OnReconnecting;
        client.Reconnected -= OnReconnected;
        client.Closed -= OnClosed;
        client.Dispose();
        SetState(AppHubConnectionState.Disconnected);
        return ValueTask.CompletedTask;
    }

    private void OnReconnecting() => SetState(AppHubConnectionState.Reconnecting);

    private async void OnReconnected()
    {
        if (disposed)
        {
            return;
        }

        SetState(AppHubConnectionState.Connected);
        try
        {
            await JoinMonitoringAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            SetState(AppHubConnectionState.Disconnected);
            LifecycleFailed?.Invoke(exception);
        }
    }

    private void OnClosed() => SetState(AppHubConnectionState.Disconnected);

    private void SetState(AppHubConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}

public interface IAppHubSignalRClient : IDisposable
{
    event Action? Reconnecting;

    event Action? Reconnected;

    event Action? Closed;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    IAppHubSignalRProxy CreateProxy(string hubName);
}

public interface IAppHubSignalRProxy
{
    IDisposable RegisterCallback(string eventName, Action<object[]> callback);

    Task JoinMonitoringAsync(CancellationToken cancellationToken);
}
