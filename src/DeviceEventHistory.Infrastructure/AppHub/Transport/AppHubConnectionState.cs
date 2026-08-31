namespace DeviceEventHistory.Infrastructure.AppHub.Transport;

public enum AppHubConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    JoiningMonitoring = 3,
    Running = 4,
    Reconnecting = 5,
    Stopping = 6
}
