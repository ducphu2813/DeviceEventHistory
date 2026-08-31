using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceConnectionEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubOpaqueEventMapper(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveStateConnected;

    protected override string Category => AppConst.Categories.DeviceConnection;
}
