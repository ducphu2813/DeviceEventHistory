using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceConnectionEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubCanonicalMapperBase(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveStateConnected;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload)
    {
        var connectionPayload = payload!.Value;
        var deviceId = ReadInt32(
            connectionPayload,
            AppConst.AppHub.PayloadFields.DeviceId);
        var isStart = ReadBoolean(connectionPayload, AppConst.AppHub.PayloadFields.IsStart);
        var isConnecting = ReadBoolean(
            connectionPayload,
            AppConst.AppHub.PayloadFields.IsConnecting);
        var isConnected = ReadBoolean(
            connectionPayload,
            AppConst.AppHub.PayloadFields.IsConnected);
        var warnings = new List<string>();

        AddMissingWarning(warnings, deviceId is > 0 ? deviceId : null);
        AddMissingWarning(warnings, isStart, isConnecting, isConnected);

        return context.CreateEvent(
            AppConst.Categories.DeviceConnection,
            new CanonicalDeviceEvent.FactsContext
            {
                Connection = new CanonicalDeviceEvent.ConnectionFacts
                {
                    Status = ResolveConnectionStatus(isConnecting, isConnected),
                    IsStart = isStart,
                    IsConnecting = isConnecting,
                    IsConnected = isConnected,
                    IsSourceConnected = isConnected
                }
            },
            CreateDevice(deviceId),
            warnings.Count == 0
                ? AppConst.Parsing.StatusParsed
                : AppConst.Parsing.StatusParsedWithWarnings,
            warnings);
    }
}
