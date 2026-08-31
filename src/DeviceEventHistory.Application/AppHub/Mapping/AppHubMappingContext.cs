using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class AppHubMappingContext
{
    private AppHubMappingContext(
        RawSourceEvent sourceEvent,
        JsonElement? payload,
        AppHubTenantResolution tenant)
    {
        SourceEvent = sourceEvent;
        Payload = payload;
        Tenant = tenant;
    }

    public RawSourceEvent SourceEvent { get; }

    public JsonElement? Payload { get; }

    public AppHubTenantResolution Tenant { get; }

    public static AppHubMappingContext Create(
        RawSourceEvent sourceEvent,
        AppHubTenantResolver tenantResolver)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);
        ArgumentNullException.ThrowIfNull(tenantResolver);

        using var document = JsonDocument.Parse(sourceEvent.RawArgumentsJson);
        var payload = GetFirstObject(document.RootElement);
        return new AppHubMappingContext(
            sourceEvent,
            payload?.Clone(),
            tenantResolver.Resolve(sourceEvent.SourceId, payload));
    }

    public static CanonicalIngestionResult CreateFailure(
        RawSourceEvent sourceEvent,
        string code,
        string message,
        string stage) =>
        CanonicalIngestionResult.FromFailure(new CanonicalIngestionFailure
        {
            FailureId = RawSourceEventIdentityFactory.CreateFailureId(sourceEvent),
            SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
            SourceKind = sourceEvent.SourceKind,
            Source = new CanonicalDeviceEvent.SourceContext
            {
                Producer = sourceEvent.SourceApplication,
                SourceId = sourceEvent.SourceId,
                Transport = sourceEvent.SourceTransport,
                EventName = sourceEvent.EventName,
                DeliveryKind = sourceEvent.DeliveryKind,
                ConnectionGeneration = sourceEvent.ConnectionGeneration,
                ReceiveSequence = sourceEvent.ReceiveSequence
            },
            RawPayload = new CanonicalDeviceEvent.RawPayloadContext
            {
                Format = AppConst.AppHub.PayloadFormat,
                ArgumentsJson = sourceEvent.RawArgumentsJson,
                Sha256 = sourceEvent.PayloadSha256,
                SizeBytes = sourceEvent.PayloadSizeBytes
            },
            Error = new CanonicalIngestionFailure.ErrorContext
            {
                Code = code,
                Message = message,
                Stage = stage,
                ParserVersion = AppConst.AppHub.ParserVersion
            },
            ReceivedAtUtc = sourceEvent.ReceivedAtUtc,
            Retryable = false
        });

    public CanonicalIngestionResult CreateEvent(
        string category,
        CanonicalDeviceEvent.FactsContext facts,
        CanonicalDeviceEvent.DeviceContext? device,
        string parseStatus,
        IReadOnlyList<string>? warnings = null,
        string? deliveryKind = null,
        DateTimeOffset? occurredAtUtc = null,
        DateTimeOffset? occurredAtLocal = null) =>
        CanonicalIngestionResult.FromEvent(new CanonicalDeviceEvent
        {
            EventId = SourceEvent.IngestionEventId,
            SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
            Category = category,
            SourceKind = SourceEvent.SourceKind,
            CompanyId = Tenant.CompanyId!.Value,
            OccurredAtUtc = occurredAtUtc,
            OccurredAtLocal = occurredAtLocal,
            ReceivedAtUtc = SourceEvent.ReceivedAtUtc,
            Source = CreateSourceContext(deliveryKind),
            Device = device,
            RawPayload = new CanonicalDeviceEvent.RawPayloadContext
            {
                Format = AppConst.AppHub.PayloadFormat,
                ArgumentsJson = SourceEvent.RawArgumentsJson,
                Sha256 = SourceEvent.PayloadSha256,
                SizeBytes = SourceEvent.PayloadSizeBytes
            },
            Facts = facts,
            Parse = new CanonicalDeviceEvent.ParseContext
            {
                Status = parseStatus,
                ParserVersion = AppConst.AppHub.ParserVersion,
                Warnings = warnings ?? []
            }
        });

    public CanonicalIngestionResult CreateFailure(
        string code,
        string message,
        string stage,
        IReadOnlyList<string>? details = null,
        int? companyId = null) =>
        CanonicalIngestionResult.FromFailure(new CanonicalIngestionFailure
        {
            FailureId = RawSourceEventIdentityFactory.CreateFailureId(SourceEvent),
            SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
            SourceKind = SourceEvent.SourceKind,
            CompanyId = companyId ?? Tenant.CompanyId,
            Source = CreateSourceContext(),
            RawPayload = new CanonicalDeviceEvent.RawPayloadContext
            {
                Format = AppConst.AppHub.PayloadFormat,
                ArgumentsJson = SourceEvent.RawArgumentsJson,
                Sha256 = SourceEvent.PayloadSha256,
                SizeBytes = SourceEvent.PayloadSizeBytes
            },
            Error = new CanonicalIngestionFailure.ErrorContext
            {
                Code = code,
                Message = message,
                Stage = stage,
                ParserVersion = AppConst.AppHub.ParserVersion,
                Details = details ?? []
            },
            ReceivedAtUtc = SourceEvent.ReceivedAtUtc,
            Retryable = false
        });

    private CanonicalDeviceEvent.SourceContext CreateSourceContext(
        string? deliveryKind = null) =>
        new()
        {
            Producer = SourceEvent.SourceApplication,
            SourceId = SourceEvent.SourceId,
            Transport = SourceEvent.SourceTransport,
            EventName = SourceEvent.EventName,
            DeliveryKind = deliveryKind ?? SourceEvent.DeliveryKind,
            ConnectionGeneration = SourceEvent.ConnectionGeneration,
            ReceiveSequence = SourceEvent.ReceiveSequence
        };

    private static JsonElement? GetFirstObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return null;
        }

        var first = root[0];
        return first.ValueKind == JsonValueKind.Object ? first : null;
    }
}
