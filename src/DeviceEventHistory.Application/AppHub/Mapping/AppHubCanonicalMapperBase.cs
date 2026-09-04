using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public abstract class AppHubCanonicalMapperBase(
    AppHubTenantResolver tenantResolver) : IRawSourceEventMapper
{
    public string SourceKind => AppConst.SourceKinds.ErpAppHub;

    public abstract string EventName { get; }

    protected virtual bool RequiresObjectPayload => true;

    public CanonicalIngestionResult Map(RawSourceEvent sourceEvent)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);

        try
        {
            var context = AppHubMappingContext.Create(sourceEvent, tenantResolver);
            if (!context.Tenant.IsResolved)
            {
                return context.CreateFailure(
                    context.Tenant.ErrorCode!,
                    context.Tenant.ErrorMessage!,
                    AppConst.IngestionStages.MetadataResolution);
            }

            if (RequiresObjectPayload && context.Payload is not JsonElement)
            {
                return context.CreateFailure(
                    AppConst.Parsing.InvalidRecordFormat,
                    AppConst.Messages.MSG_APPHUB_PAYLOAD_ARGUMENT_REQUIRED,
                    AppConst.IngestionStages.Validation);
            }

            return MapPayload(context, context.Payload);
        }
        catch (JsonException)
        {
            return CreateFailure(
                sourceEvent,
                AppConst.Parsing.InvalidRecordFormat,
                AppConst.Messages.MSG_APPHUB_PAYLOAD_JSON_INVALID,
                AppConst.IngestionStages.Deserialization);
        }
    }

    protected abstract CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload);

    protected static CanonicalIngestionResult CreateFailure(
        RawSourceEvent sourceEvent,
        string code,
        string message,
        string stage) =>
        AppHubMappingContext.CreateFailure(sourceEvent, code, message, stage);

    protected static int? ReadInt32(JsonElement payload, string propertyName) =>
        AppHubJsonValueReader.TryGetProperty(payload, propertyName, out var value)
            ? AppHubJsonValueReader.ReadInt32(value)
            : null;

    protected static string? ReadString(JsonElement payload, string propertyName) =>
        AppHubJsonValueReader.TryGetProperty(payload, propertyName, out var value)
            ? AppHubJsonValueReader.ReadString(value)
            : null;

    protected static bool? ReadBoolean(JsonElement payload, string propertyName) =>
        AppHubJsonValueReader.TryGetProperty(payload, propertyName, out var value)
            ? AppHubJsonValueReader.ReadBoolean(value)
            : null;

    protected static double? ReadDouble(JsonElement payload, string propertyName) =>
        AppHubJsonValueReader.TryGetProperty(payload, propertyName, out var value)
            ? AppHubJsonValueReader.ReadDouble(value)
            : null;

    protected static void AddMissingWarning(
        ICollection<string> warnings,
        params object?[] values)
    {
        if (values.Any(value => value is null))
        {
            warnings.Add(AppConst.Parsing.OptionalFieldMissing);
        }
    }

    protected static CanonicalDeviceEvent.DeviceContext? CreateDevice(int? deviceId)
    {
        return deviceId is > 0
            ? new CanonicalDeviceEvent.DeviceContext { Id = deviceId }
            : null;
    }

    protected static string ResolveConnectionStatus(
        bool? isConnecting,
        bool? isConnected) =>
        isConnecting == true
            ? AppConst.CanonicalValues.ConnectionStatusConnecting
            : isConnected == true
                ? AppConst.CanonicalValues.ConnectionStatusConnected
                : isConnected == false
                    ? AppConst.CanonicalValues.ConnectionStatusDisconnected
                    : AppConst.CanonicalValues.ConnectionStatusUnknown;
}
