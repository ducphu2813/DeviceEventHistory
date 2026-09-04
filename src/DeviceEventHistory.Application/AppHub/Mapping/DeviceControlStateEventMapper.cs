using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceControlStateEventMapper(
    AppHubTenantResolver tenantResolver,
    string eventName) : AppHubCanonicalMapperBase(tenantResolver)
{
    public override string EventName => eventName;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload)
    {
        var controlPayload = payload!.Value;
        var deviceId = ReadInt32(
            controlPayload,
            AppConst.AppHub.PayloadFields.DeviceId);
        var isOn = ReadBoolean(controlPayload, AppConst.AppHub.PayloadFields.On);
        var warnings = new List<string>();
        AddMissingWarning(warnings, deviceId is > 0 ? deviceId : null, isOn);

        var control = string.Equals(
            EventName,
            AppConst.AppHub.Callbacks.ReceiveGreenState,
            StringComparison.Ordinal)
            ? AppConst.CanonicalValues.DeviceControlGreenLight
            : AppConst.CanonicalValues.DeviceControlRedLight;
        var state = isOn.HasValue
            ? isOn.Value
                ? AppConst.CanonicalValues.DeviceControlStateOn
                : AppConst.CanonicalValues.DeviceControlStateOff
            : AppConst.CanonicalValues.ConnectionStatusUnknown;

        return context.CreateEvent(
            AppConst.Categories.DeviceControlState,
            new CanonicalDeviceEvent.FactsContext
            {
                DeviceControlState = new CanonicalDeviceEvent.DeviceControlStateFacts
                {
                    Control = control,
                    State = state,
                    RawState = isOn?.ToString().ToLowerInvariant()
                }
            },
            CreateDevice(deviceId),
            warnings.Count == 0
                ? AppConst.Parsing.StatusParsed
                : AppConst.Parsing.StatusParsedWithWarnings,
            warnings);
    }
}
