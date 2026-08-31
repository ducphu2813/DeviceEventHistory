using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceOnlineEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubOpaqueEventMapper(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveDeviceOnline;

    protected override string Category => AppConst.Categories.DeviceOnline;

    protected override string DeliveryKind => AppConst.DeliveryKinds.SnapshotCandidate;
}
