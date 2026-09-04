using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class ClientDeviceConnectionEventMapper(
    AppHubTenantResolver tenantResolver,
    string eventName) : AppHubCanonicalMapperBase(tenantResolver)
{
    public override string EventName => eventName;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload)
    {
        var clientPayload = payload!.Value;
        var deviceId = ReadInt32(
            clientPayload,
            AppConst.AppHub.PayloadFields.DeviceId);
        var gateId = ReadInt32(clientPayload, AppConst.AppHub.PayloadFields.GateId);
        var warnings = new List<string>();
        AddMissingWarning(warnings, deviceId is > 0 ? deviceId : null, gateId);

        var isConnected = string.Equals(
            EventName,
            AppConst.AppHub.Callbacks.ReceiveClientDeviceConnected,
            StringComparison.Ordinal);

        return context.CreateEvent(
            AppConst.Categories.ClientDeviceConnection,
            new CanonicalDeviceEvent.FactsContext
            {
                Connection = new CanonicalDeviceEvent.ConnectionFacts
                {
                    Status = isConnected
                        ? AppConst.CanonicalValues.ConnectionStatusConnected
                        : AppConst.CanonicalValues.ConnectionStatusDisconnected,
                    IsConnected = isConnected,
                    IsSourceConnected = isConnected
                }
            },
            deviceId is > 0
                ? new CanonicalDeviceEvent.DeviceContext { Id = deviceId, GateId = gateId }
                : null,
            warnings.Count == 0
                ? AppConst.Parsing.StatusParsed
                : AppConst.Parsing.StatusParsedWithWarnings,
            warnings);
    }
}
