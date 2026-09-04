using System.Text.Json;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class ScannerEventMapper(
    AppHubTenantResolver tenantResolver,
    string eventName) : AppHubCanonicalMapperBase(tenantResolver)
{
    public override string EventName => eventName;

    protected override CanonicalIngestionResult MapPayload(
        AppHubMappingContext context,
        JsonElement? payload)
    {
        var scannerPayload = payload!.Value;
        var deviceId = ReadInt32(
            scannerPayload,
            AppConst.AppHub.UserState.DeviceId);
        if (deviceId is not > 0)
        {
            return context.CreateFailure(
                AppConst.Parsing.InvalidRecordFormat,
                AppConst.Messages.Format(
                    AppConst.Messages.MSG_APPHUB_REQUIRED_FIELD_MISSING,
                    EventName,
                    AppConst.AppHub.UserState.DeviceId),
                AppConst.IngestionStages.Validation);
        }

        var warnings = new List<string>();
        var connectedAtLocal = ReadConnectedAtLocal(scannerPayload, warnings);
        var connectionIdHash = ReadString(
            scannerPayload,
            AppConst.AppHub.UserState.ConnectionIdHash);
        if (string.IsNullOrWhiteSpace(connectionIdHash))
        {
            warnings.Add(AppConst.Parsing.ScannerConnectionIdMissing);
        }

        var sessionType = ReadInt32(
            scannerPayload,
            AppConst.AppHub.UserState.SessionType);
        var deviceType = ReadInt32(
            scannerPayload,
            AppConst.AppHub.UserState.DeviceType);
        var userId = ReadInt32(
            scannerPayload,
            AppConst.AppHub.UserState.UserId);
        if (sessionType is null || deviceType is null || userId is null)
        {
            warnings.Add(AppConst.Parsing.OptionalFieldMissing);
        }

        var isSnapshot = string.Equals(
            EventName,
            AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline,
            StringComparison.Ordinal);
        var status = isSnapshot
            ? AppConst.CanonicalValues.ConnectionStatusUnknown
            : string.Equals(
                EventName,
                AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
                StringComparison.Ordinal)
                ? AppConst.CanonicalValues.ConnectionStatusConnected
                : AppConst.CanonicalValues.ConnectionStatusDisconnected;

        var facts = new CanonicalDeviceEvent.FactsContext
        {
            Connection = new CanonicalDeviceEvent.ConnectionFacts
            {
                Status = status,
                IsSourceConnected = isSnapshot
                    ? null
                    : status == AppConst.CanonicalValues.ConnectionStatusConnected,
                ConnectedAtLocal = connectedAtLocal
            },
            Scanner = new CanonicalDeviceEvent.ScannerFacts
            {
                SessionType = sessionType,
                DeviceType = deviceType,
                ConnectionIdHash = connectionIdHash
            },
            User = userId is null
                ? null
                : new CanonicalDeviceEvent.UserFacts { UserId = userId }
        };

        return context.CreateEvent(
            isSnapshot ? AppConst.Categories.DeviceSnapshot : AppConst.Categories.ScannerConnection,
            facts,
            new CanonicalDeviceEvent.DeviceContext
            {
                Id = deviceId,
                Type = AppConst.CanonicalValues.ScannerDeviceType,
                Name = ReadString(
                    scannerPayload,
                    AppConst.AppHub.UserState.DeviceName),
                GateId = ReadInt32(
                    scannerPayload,
                    AppConst.AppHub.UserState.GateId),
                GateName = ReadString(
                    scannerPayload,
                    AppConst.AppHub.UserState.GateName)
            },
            warnings.Count == 0
                ? AppConst.Parsing.StatusParsed
                : AppConst.Parsing.StatusParsedWithWarnings,
            warnings,
            isSnapshot
                ? AppConst.DeliveryKinds.Snapshot
                : AppConst.AppHub.DeliveryKind);
    }

    private static DateTimeOffset? ReadConnectedAtLocal(
        JsonElement payload,
        ICollection<string> warnings)
    {
        if (!AppHubJsonValueReader.TryGetProperty(
                payload,
                AppConst.AppHub.UserState.DateConnected,
                out var value))
        {
            warnings.Add(AppConst.Parsing.SourceTimeMissing);
            return null;
        }

        var localDateTime = AppHubJsonValueReader.ReadLocalDateTime(value);
        if (localDateTime is null)
        {
            warnings.Add(AppConst.Parsing.SourceTimeUntrusted);
            return null;
        }

        warnings.Add(AppConst.Parsing.SourceTimeUntrusted);
        return localDateTime;
    }
}
