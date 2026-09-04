using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class DeviceReadTagEventMapper(AppHubTenantResolver tenantResolver)
    : AppHubCanonicalMapperBase(tenantResolver)
{
    public override string EventName => AppConst.AppHub.Callbacks.ReceiveDeviceReadTag;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload)
    {
        var tagPayload = payload!.Value;
        var deviceId = ReadInt32(tagPayload, AppConst.AppHub.PayloadFields.DeviceId);
        var tagId = ReadString(tagPayload, AppConst.AppHub.PayloadFields.TagId);
        var epc = ReadString(tagPayload, AppConst.AppHub.PayloadFields.Epc);
        var warnings = new List<string>();

        if (deviceId is not > 0)
        {
            warnings.Add(AppConst.Parsing.OptionalFieldMissing);
        }

        if (string.IsNullOrWhiteSpace(tagId) && string.IsNullOrWhiteSpace(epc))
        {
            warnings.Add(AppConst.Parsing.OptionalFieldMissing);
        }

        return context.CreateEvent(
            AppConst.Categories.TagRead,
            new CanonicalDeviceEvent.FactsContext
            {
                TagRead = string.IsNullOrWhiteSpace(tagId) && string.IsNullOrWhiteSpace(epc)
                    ? null
                    : new CanonicalDeviceEvent.TagReadFacts
                    {
                        TagId = tagId,
                        EpcRaw = epc
                    }
            },
            deviceId is > 0
                ? new CanonicalDeviceEvent.DeviceContext { Id = deviceId }
                : null,
            warnings.Count == 0
                ? AppConst.Parsing.StatusParsed
                : AppConst.Parsing.StatusParsedWithWarnings,
            warnings);
    }
}
