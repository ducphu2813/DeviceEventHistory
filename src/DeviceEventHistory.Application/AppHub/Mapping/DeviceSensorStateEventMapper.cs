using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceSensorStateEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubCanonicalMapperBase(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveTimeSensor;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload)
    {
        var sensorPayload = payload!.Value;
        var deviceId = ReadInt32(
            sensorPayload,
            AppConst.AppHub.PayloadFields.DeviceId);
        var timeout = ReadDouble(sensorPayload, AppConst.AppHub.PayloadFields.Timeout);
        var hasTimeoutField = AppHubJsonValueReader.TryGetProperty(
            sensorPayload,
            AppConst.AppHub.PayloadFields.Timeout,
            out _);
        var warnings = new List<string>();
        AddMissingWarning(warnings, deviceId is > 0 ? deviceId : null);
        if (!hasTimeoutField)
        {
            warnings.Add(AppConst.Parsing.OptionalFieldMissing);
        }

        return context.CreateEvent(
            AppConst.Categories.DeviceSensorState,
            new CanonicalDeviceEvent.FactsContext
            {
                SensorState = new CanonicalDeviceEvent.SensorStateFacts
                {
                    Sensor = AppConst.CanonicalValues.DeviceSensorTime,
                    State = timeout.HasValue
                        ? AppConst.CanonicalValues.DeviceSensorStateTimeout
                        : AppConst.CanonicalValues.DeviceSensorStateActive,
                    Timeout = timeout,
                    TimeoutUnit = timeout.HasValue
                        ? AppConst.CanonicalValues.DeviceSensorTimeoutUnitSeconds
                        : null
                }
            },
            CreateDevice(deviceId),
            warnings.Count == 0
                ? AppConst.Parsing.StatusParsed
                : AppConst.Parsing.StatusParsedWithWarnings,
            warnings);
    }
}
