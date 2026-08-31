using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class ClientDeviceConnectionEventMapper(
    AppHubTenantResolver tenantResolver,
    string eventName) : AppHubOpaqueEventMapper(tenantResolver)
{
    public override string EventName => eventName;

    protected override string Category => AppConst.Categories.ClientDeviceConnection;
}
