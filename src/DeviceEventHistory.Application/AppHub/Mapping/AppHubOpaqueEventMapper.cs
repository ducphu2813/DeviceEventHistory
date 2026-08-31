using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public abstract class AppHubOpaqueEventMapper(
    AppHubTenantResolver tenantResolver) : AppHubCanonicalMapperBase(tenantResolver)
{
    protected abstract string Category { get; }

    protected virtual string DeliveryKind => AppConst.AppHub.DeliveryKind;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload) =>
        context.CreateEvent(
            Category,
            new CanonicalDeviceEvent.FactsContext(),
            device: null,
            AppConst.Parsing.StatusUnmapped,
            [AppConst.Parsing.AppHubOpaqueContractUnconfirmed],
            DeliveryKind);
}
