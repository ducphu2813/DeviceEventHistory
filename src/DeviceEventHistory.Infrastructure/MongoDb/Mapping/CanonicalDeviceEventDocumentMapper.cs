using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DeviceEventHistory.Infrastructure.MongoDb.Mapping;

internal static class CanonicalDeviceEventDocumentMapper
{
    public static BsonDocument ToDocument(
        CanonicalDeviceEvent deviceEvent,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset persistedAtUtc,
        string workerId)
    {
        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "eventId", deviceEvent.EventId },
            { "schemaVersion", deviceEvent.SchemaVersion },
            { "category", deviceEvent.Category },
            { "sourceKind", deviceEvent.SourceKind },
            { "companyId", deviceEvent.CompanyId },
            { "occurredAtUtc", MongoDocumentValue.DateTimeOffset(deviceEvent.OccurredAtUtc) },
            { "occurredAtLocal", MongoDocumentValue.String(
                deviceEvent.OccurredAtLocal?.ToString(AppConst.Identity.IsoDateTimeFormat)) },
            { "receivedAtUtc", MongoDocumentValue.DateTimeOffset(
                deviceEvent.ReceivedAtUtc ?? receivedAtUtc) },
            { "persistedAtUtc", MongoDocumentValue.DateTimeOffset(
                deviceEvent.PersistedAtUtc ?? persistedAtUtc) },
            { "timelineAtUtc", MongoDocumentValue.DateTimeOffset(deviceEvent.TimelineAtUtc) },
            { "timeBasis", MongoDocumentValue.String(deviceEvent.TimeBasis) },
            { "source", MapSource(deviceEvent.Source) },
            { "rawPayload", MapRawPayload(deviceEvent.RawPayload) },
            { "facts", MapFacts(deviceEvent.Facts) },
            { "parse", MapParse(deviceEvent.Parse) },
            { "ingestion", new BsonDocument
                {
                    { "workerId", workerId },
                    { "attempt", deviceEvent.Ingestion?.Attempt ?? 1 },
                    { "processingDurationMs", MongoDocumentValue.Int64(
                        deviceEvent.Ingestion?.ProcessingDurationMs) }
                } }
        };

        AddOptional(document, "device", MapDevice(deviceEvent.Device));
        return document;
    }

    private static BsonDocument MapSource(CanonicalDeviceEvent.SourceContext source)
    {
        var document = new BsonDocument
        {
            { "producer", source.Producer },
            { "sourceId", source.SourceId }
        };
        AddOptional(document, "transport", MongoDocumentValue.String(source.Transport));
        AddOptional(document, "eventName", MongoDocumentValue.String(source.EventName));
        AddOptional(document, "sourceEventId", MongoDocumentValue.String(source.SourceEventId));
        AddOptional(document, "deliveryKind", MongoDocumentValue.String(source.DeliveryKind));
        AddOptional(document, "connectionGeneration", MongoDocumentValue.String(
            source.ConnectionGeneration));
        AddOptional(document, "receiveSequence", MongoDocumentValue.Int64(source.ReceiveSequence));
        AddOptional(document, "fileId", MongoDocumentValue.Int64(source.FileId));
        AddOptional(document, "fileName", MongoDocumentValue.String(source.FileName));
        AddOptional(document, "relativePath", MongoDocumentValue.String(source.RelativePath));
        AddOptional(document, "folderDate", source.FolderDate is DateOnly folderDate
            ? MongoDocumentValue.DateOnly(folderDate)
            : null);
        AddOptional(document, "offsetStart", MongoDocumentValue.Int64(source.OffsetStart));
        AddOptional(document, "offsetEnd", MongoDocumentValue.Int64(source.OffsetEnd));
        return document;
    }

    private static BsonDocument? MapDevice(CanonicalDeviceEvent.DeviceContext? device)
    {
        if (device is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "id", MongoDocumentValue.Int32(device.Id));
        AddOptional(document, "gateId", MongoDocumentValue.Int32(device.GateId));
        AddOptional(document, "type", MongoDocumentValue.String(device.Type));
        AddOptional(document, "code", MongoDocumentValue.String(device.Code));
        AddOptional(document, "name", MongoDocumentValue.String(device.Name));
        AddOptional(document, "gateCode", MongoDocumentValue.String(device.GateCode));
        AddOptional(document, "gateName", MongoDocumentValue.String(device.GateName));
        return document;
    }

    private static BsonDocument MapRawPayload(CanonicalDeviceEvent.RawPayloadContext rawPayload)
    {
        var document = new BsonDocument { { "format", rawPayload.Format } };
        AddOptional(document, "text", MongoDocumentValue.String(rawPayload.Text));
        AddOptional(document, "arguments", ParseArguments(rawPayload.ArgumentsJson));
        AddOptional(document, "sha256", rawPayload.Sha256);
        AddOptional(document, "sizeBytes", MongoDocumentValue.Int64(rawPayload.SizeBytes));
        return document;
    }

    private static BsonValue? ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return null;
        }

        try
        {
            return BsonSerializer.Deserialize<BsonValue>(argumentsJson);
        }
        catch (Exception exception) when (
            exception is BsonSerializationException
                or FormatException
                or InvalidOperationException)
        {
            return new BsonString(argumentsJson);
        }
    }

    private static BsonDocument MapFacts(CanonicalDeviceEvent.FactsContext facts)
    {
        var document = new BsonDocument();
        AddOptional(document, "tagRead", MapTagRead(facts.TagRead));
        AddOptional(document, "gateState", MapGateState(facts.GateState));
        AddOptional(document, "signal", MapSignal(facts.Signal));
        AddOptional(document, "businessEvent", MapBusinessEvent(facts.BusinessEvent));
        AddOptional(document, "styleProcess", MapStyleProcess(facts.StyleProcess));
        AddOptional(document, "user", MapUser(facts.User));
        AddOptional(document, "connection", MapConnection(facts.Connection));
        AddOptional(document, "deviceOnline", MapDeviceOnline(facts.DeviceOnline));
        AddOptional(document, "deviceControlState", MapDeviceControlState(facts.DeviceControlState));
        AddOptional(document, "sensorState", MapSensorState(facts.SensorState));
        AddOptional(document, "scanner", MapScanner(facts.Scanner));
        AddOptional(document, "deviceError", MapDeviceError(facts.DeviceError));
        return document;
    }

    private static BsonDocument? MapTagRead(CanonicalDeviceEvent.TagReadFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "tagId", MongoDocumentValue.String(facts.TagId));
        AddOptional(document, "epcRaw", MongoDocumentValue.String(facts.EpcRaw));
        AddOptional(document, "routingFileId", MongoDocumentValue.Int64(facts.RoutingFileId));
        AddOptional(document, "readTimeText", MongoDocumentValue.String(facts.ReadTimeText));
        return document;
    }

    private static BsonDocument? MapGateState(CanonicalDeviceEvent.GateStateFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "stateCode", MongoDocumentValue.Int32(facts.StateCode));
        AddOptional(document, "rawValue", MongoDocumentValue.String(facts.RawValue));
        return document;
    }

    private static BsonDocument? MapSignal(CanonicalDeviceEvent.SignalFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "antennaPort", MongoDocumentValue.Int32(facts.AntennaPort));
        AddOptional(document, "firstSeenAtLocal", MongoDocumentValue.String(
            facts.FirstSeenAtLocal?.ToString(AppConst.Identity.IsoDateTimeFormat)));
        AddOptional(document, "lastSeenAtLocal", MongoDocumentValue.String(
            facts.LastSeenAtLocal?.ToString(AppConst.Identity.IsoDateTimeFormat)));
        AddOptional(document, "seenCount", MongoDocumentValue.Int32(facts.SeenCount));
        AddOptional(document, "txPower", MongoDocumentValue.Int32(facts.TxPower));
        AddOptional(document, "dopplerFrequency", MongoDocumentValue.Double(facts.DopplerFrequency));
        AddOptional(document, "phaseAngle", MongoDocumentValue.Double(facts.PhaseAngle));
        AddOptional(document, "channelMhz", MongoDocumentValue.Double(facts.ChannelMhz));
        AddOptional(document, "peakRssiDbm", MongoDocumentValue.Double(facts.PeakRssiDbm));
        return document;
    }

    private static BsonDocument? MapBusinessEvent(
        CanonicalDeviceEvent.BusinessEventFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "eventType", MongoDocumentValue.Int32(facts.EventType));
        AddOptional(document, "processId", MongoDocumentValue.Int32(facts.ProcessId));
        AddOptional(document, "quantity", MongoDocumentValue.Int32(facts.Quantity));
        AddOptional(document, "processIdsRaw", MongoDocumentValue.String(facts.ProcessIdsRaw));
        AddOptional(document, "processIds", facts.ProcessIds is null
            ? null
            : MongoDocumentValue.Int32Array(facts.ProcessIds));
        AddOptional(document, "second", MongoDocumentValue.Int32(facts.Second));
        return document;
    }

    private static BsonDocument? MapStyleProcess(CanonicalDeviceEvent.StyleProcessFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "processCustomRaw", MongoDocumentValue.String(facts.ProcessCustomRaw));
        AddOptional(document, "processCustom", facts.ProcessCustom is null
            ? null
            : MongoDocumentValue.Int32Array(facts.ProcessCustom));
        return document;
    }

    private static BsonDocument? MapUser(CanonicalDeviceEvent.UserFacts? facts) =>
        facts?.UserId is int userId
            ? new BsonDocument("userId", userId)
            : null;

    private static BsonDocument? MapConnection(CanonicalDeviceEvent.ConnectionFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "status", MongoDocumentValue.String(facts.Status));
        AddOptional(document, "reason", MongoDocumentValue.String(facts.Reason));
        AddOptional(document, "isStart", facts.IsStart is bool isStart
            ? new BsonBoolean(isStart)
            : null);
        AddOptional(document, "isConnecting", facts.IsConnecting is bool isConnecting
            ? new BsonBoolean(isConnecting)
            : null);
        AddOptional(document, "isConnected", facts.IsConnected is bool isConnected
            ? new BsonBoolean(isConnected)
            : null);
        AddOptional(document, "isSourceConnected", facts.IsSourceConnected is bool connected
            ? new BsonBoolean(connected)
            : null);
        AddOptional(document, "connectedAtLocal", MongoDocumentValue.String(
            facts.ConnectedAtLocal?.ToString(AppConst.Identity.IsoDateTimeFormat)));
        return document;
    }

    private static BsonDocument? MapDeviceOnline(
        CanonicalDeviceEvent.DeviceOnlineFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "online", facts.Online is bool online ? new BsonBoolean(online) : null);
        AddOptional(document, "active", facts.Active is bool active ? new BsonBoolean(active) : null);
        AddOptional(document, "snapshot", facts.IsSnapshot is bool snapshot
            ? new BsonBoolean(snapshot)
            : null);
        AddOptional(document, "sourceState", MongoDocumentValue.String(facts.SourceState));
        AddOptional(document, "isStart", facts.IsStart is bool isStart
            ? new BsonBoolean(isStart)
            : null);
        AddOptional(document, "isUsed", facts.IsUsed is bool isUsed
            ? new BsonBoolean(isUsed)
            : null);
        AddOptional(document, "isConnecting", facts.IsConnecting is bool isConnecting
            ? new BsonBoolean(isConnecting)
            : null);
        AddOptional(document, "isConnected", facts.IsConnected is bool isConnected
            ? new BsonBoolean(isConnected)
            : null);
        AddOptional(document, "isGreenLighting", facts.IsGreenLighting is bool isGreenLighting
            ? new BsonBoolean(isGreenLighting)
            : null);
        AddOptional(document, "isRedLighting", facts.IsRedLighting is bool isRedLighting
            ? new BsonBoolean(isRedLighting)
            : null);
        AddOptional(document, "gateState", MongoDocumentValue.String(facts.GateState));
        return document;
    }

    private static BsonDocument? MapDeviceControlState(
        CanonicalDeviceEvent.DeviceControlStateFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "control", MongoDocumentValue.String(facts.Control));
        AddOptional(document, "state", MongoDocumentValue.String(facts.State));
        AddOptional(document, "rawState", MongoDocumentValue.String(facts.RawState));
        return document;
    }

    private static BsonDocument? MapSensorState(CanonicalDeviceEvent.SensorStateFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "sensor", MongoDocumentValue.String(facts.Sensor));
        AddOptional(document, "state", MongoDocumentValue.String(facts.State));
        AddOptional(document, "timeout", MongoDocumentValue.Double(facts.Timeout));
        AddOptional(document, "timeoutUnit", MongoDocumentValue.String(facts.TimeoutUnit));
        return document;
    }

    private static BsonDocument? MapScanner(CanonicalDeviceEvent.ScannerFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "sessionType", MongoDocumentValue.Int32(facts.SessionType));
        AddOptional(document, "deviceType", MongoDocumentValue.Int32(facts.DeviceType));
        AddOptional(document, "connectionIdHash", MongoDocumentValue.String(facts.ConnectionIdHash));
        return document;
    }

    private static BsonDocument? MapDeviceError(CanonicalDeviceEvent.DeviceErrorFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        var document = new BsonDocument();
        AddOptional(document, "code", MongoDocumentValue.String(facts.Code));
        AddOptional(document, "message", MongoDocumentValue.String(facts.Message));
        AddOptional(document, "severity", MongoDocumentValue.String(facts.Severity));
        AddOptional(document, "retryable", facts.Retryable is bool retryable
            ? new BsonBoolean(retryable)
            : null);
        return document;
    }

    private static BsonDocument MapParse(CanonicalDeviceEvent.ParseContext parse) => new()
    {
        { "status", parse.Status },
        { "parserVersion", parse.ParserVersion },
        { "warnings", MongoDocumentValue.StringArray(parse.Warnings) },
        { "errors", MongoDocumentValue.StringArray(parse.Errors) }
    };

    private static void AddOptional(
        BsonDocument document,
        string name,
        BsonValue? value)
    {
        if (value is not null && !value.IsBsonNull)
        {
            document.Add(name, value);
        }
    }
}
