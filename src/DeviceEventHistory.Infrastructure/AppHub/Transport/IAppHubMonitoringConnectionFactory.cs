using DeviceEventHistory.Infrastructure.AppHub.Configuration;

namespace DeviceEventHistory.Infrastructure.AppHub.Transport;

public interface IAppHubMonitoringConnectionFactory
{
    IAppHubMonitoringConnection Create(AppHubSourceOptions source);
}
