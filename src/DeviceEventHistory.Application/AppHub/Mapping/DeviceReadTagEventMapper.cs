using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceReadTagEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubOpaqueEventMapper(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveDeviceReadTag;

    protected override string Category => AppConst.Categories.TagRead;
}
