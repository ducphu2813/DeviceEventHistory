using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceOnlineEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubCanonicalMapperBase(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveDeviceOnline;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload)
    {
        var onlinePayload = payload!.Value;
        var deviceId = ReadInt32(
            onlinePayload,
            AppConst.AppHub.PayloadFields.DeviceId);
        var gateId = ReadInt32(onlinePayload, AppConst.AppHub.PayloadFields.GateId);
        var isStart = ReadBoolean(onlinePayload, AppConst.AppHub.PayloadFields.IsStart);
        var isUsed = ReadBoolean(onlinePayload, AppConst.AppHub.PayloadFields.IsUsed);
        var isConnecting = ReadBoolean(
            onlinePayload,
            AppConst.AppHub.PayloadFields.IsConnecting);
        var isConnected = ReadBoolean(
            onlinePayload,
            AppConst.AppHub.PayloadFields.IsConnected);
        var isGreenLighting = ReadBoolean(
            onlinePayload,
            AppConst.AppHub.PayloadFields.IsGreenLighting);
        var isRedLighting = ReadBoolean(
            onlinePayload,
            AppConst.AppHub.PayloadFields.IsRedLighting);
        var isActive = ReadBoolean(onlinePayload, AppConst.AppHub.PayloadFields.IsActive);
        var gateState = ReadString(onlinePayload, AppConst.AppHub.PayloadFields.GateState);
        var warnings = new List<string>();

        AddMissingWarning(warnings, deviceId is > 0 ? deviceId : null);
        AddMissingWarning(
            warnings,
            isStart,
            isUsed,
            isConnecting,
            isConnected,
            isGreenLighting,
            isRedLighting,
            isActive,
            gateId);

        return context.CreateEvent(
            AppConst.Categories.DeviceOnline,
            new CanonicalDeviceEvent.FactsContext
            {
                DeviceOnline = new CanonicalDeviceEvent.DeviceOnlineFacts
                {
                    Online = isConnected,
                    Active = isActive,
                    IsSnapshot = null,
                    SourceState = ResolveConnectionStatus(isConnecting, isConnected),
                    IsStart = isStart,
                    IsUsed = isUsed,
                    IsConnecting = isConnecting,
                    IsConnected = isConnected,
                    IsGreenLighting = isGreenLighting,
                    IsRedLighting = isRedLighting,
                    GateState = gateState
                }
            },
            deviceId is > 0
                ? new CanonicalDeviceEvent.DeviceContext
                {
                    Id = deviceId,
                    GateId = gateId,
                    Code = ReadString(onlinePayload, AppConst.AppHub.PayloadFields.DeviceCode),
                    Name = ReadString(onlinePayload, AppConst.AppHub.PayloadFields.DeviceName),
                    GateCode = ReadString(onlinePayload, AppConst.AppHub.PayloadFields.GateCode),
                    GateName = ReadString(onlinePayload, AppConst.AppHub.PayloadFields.GateName)
                }
                : null,
            warnings.Count == 0
                ? AppConst.Parsing.StatusParsed
                : AppConst.Parsing.StatusParsedWithWarnings,
            warnings,
            AppConst.DeliveryKinds.SnapshotCandidate);
    }
}
