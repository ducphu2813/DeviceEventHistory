using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceSensorStateEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubOpaqueEventMapper(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveTimeSensor;

    protected override string Category => AppConst.Categories.DeviceSensorState;
}
