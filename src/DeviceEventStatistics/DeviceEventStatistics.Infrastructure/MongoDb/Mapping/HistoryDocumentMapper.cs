using System.Globalization;
using DeviceEventStatistics.Application.History;
using MongoDB.Bson;

namespace DeviceEventStatistics.Infrastructure.MongoDb.Mapping;

public sealed class HistoryDocumentMapper
{
    public HistoryEvent Map(BsonDocument document)
    {
        var diagnostics = new List<string>();
        var sourceDocumentId = ReadSourceDocumentId(document, diagnostics);
        var source = ReadDocument(document, "source", diagnostics);
        var device = ReadDocument(document, "device", diagnostics);
        var facts = ReadDocument(document, "facts", diagnostics);
        var parse = ReadDocument(document, "parse", diagnostics);

        var eventId = ReadString(document, "eventId", diagnostics);
        if (eventId is not null && !IsLowercaseSha256(eventId))
        {
            diagnostics.Add("STAT_EVENT_ID_INVALID");
            eventId = null;
        }

        return new HistoryEvent
        {
            SourceDocumentId = sourceDocumentId,
            EventId = eventId,
            SchemaVersion = ReadInt32(document, "schemaVersion", diagnostics),
            CompanyId = ReadInt64(document, "companyId", diagnostics),
            Category = ReadString(document, "category", diagnostics),
            SourceKind = ReadString(document, "sourceKind", diagnostics),
            OccurredAtUtc = ReadDateTimeOffset(document, "occurredAtUtc", diagnostics),
            ReceivedAtUtc = ReadDateTimeOffset(document, "receivedAtUtc", diagnostics),
            PersistedAtUtc = ReadDateTimeOffset(document, "persistedAtUtc", diagnostics),
            TimelineAtUtc = ReadDateTimeOffset(document, "timelineAtUtc", diagnostics),
            TimeBasis = ReadString(document, "timeBasis", diagnostics),
            SourceId = ReadString(source, "sourceId", diagnostics, "source.sourceId"),
            SourceEventName = ReadString(source, "eventName", diagnostics, "source.eventName"),
            DeliveryKind = ReadString(source, "deliveryKind", diagnostics, "source.deliveryKind"),
            DeviceId = ReadInt64(device, "id", diagnostics, "device.id"),
            GateId = ReadInt64(device, "gateId", diagnostics, "device.gateId"),
            DeviceType = ReadString(device, "type", diagnostics, "device.type"),
            DeviceCode = ReadString(device, "code", diagnostics, "device.code"),
            DeviceName = ReadString(device, "name", diagnostics, "device.name"),
            GateCode = ReadString(device, "gateCode", diagnostics, "device.gateCode"),
            GateName = ReadString(device, "gateName", diagnostics, "device.gateName"),
            ParseStatus = ReadString(parse, "status", diagnostics, "parse.status"),
            Facts = ReadFacts(facts, diagnostics),
            MappingDiagnostics = diagnostics
        };
    }

    private static bool IsLowercaseSha256(string value) =>
        value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static HistoryFacts ReadFacts(BsonDocument? facts, ICollection<string> diagnostics) =>
        facts is null
            ? new HistoryFacts()
            : new()
        {
            TagRead = ReadTagRead(ReadDocument(facts, "tagRead", diagnostics, "facts.tagRead"), diagnostics),
            BusinessEvent = ReadBusinessEvent(ReadDocument(facts, "businessEvent", diagnostics, "facts.businessEvent"), diagnostics),
            Connection = ReadConnection(ReadDocument(facts, "connection", diagnostics, "facts.connection"), diagnostics),
            DeviceOnline = ReadDeviceOnline(ReadDocument(facts, "deviceOnline", diagnostics, "facts.deviceOnline"), diagnostics),
            DeviceControlState = ReadDeviceControl(ReadDocument(facts, "deviceControlState", diagnostics, "facts.deviceControlState"), diagnostics),
            SensorState = ReadSensor(ReadDocument(facts, "sensorState", diagnostics, "facts.sensorState"), diagnostics),
            Scanner = ReadScanner(ReadDocument(facts, "scanner", diagnostics, "facts.scanner"), diagnostics),
            DeviceError = ReadDeviceError(ReadDocument(facts, "deviceError", diagnostics, "facts.deviceError"), diagnostics)
        };

    private static TagReadFacts? ReadTagRead(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new TagReadFacts(
            ReadString(document, "tagId", diagnostics, "facts.tagRead.tagId"),
            ReadString(document, "epcRaw", diagnostics, "facts.tagRead.epcRaw"),
            ReadInt64(document, "routingFileId", diagnostics, "facts.tagRead.routingFileId"));

    private static BusinessEventFacts? ReadBusinessEvent(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new BusinessEventFacts(
            ReadInt32(document, "eventType", diagnostics, "facts.businessEvent.eventType"),
            ReadInt32(document, "processId", diagnostics, "facts.businessEvent.processId"),
            ReadInt32(document, "quantity", diagnostics, "facts.businessEvent.quantity"));

    private static ConnectionFacts? ReadConnection(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new ConnectionFacts(
            ReadString(document, "status", diagnostics, "facts.connection.status"),
            ReadBoolean(document, "isConnecting", diagnostics, "facts.connection.isConnecting"),
            ReadBoolean(document, "isConnected", diagnostics, "facts.connection.isConnected"),
            ReadBoolean(document, "isSourceConnected", diagnostics, "facts.connection.isSourceConnected"));

    private static DeviceOnlineFacts? ReadDeviceOnline(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new DeviceOnlineFacts(
            ReadBoolean(document, "online", diagnostics, "facts.deviceOnline.online"),
            ReadBoolean(document, "active", diagnostics, "facts.deviceOnline.active"),
            ReadBoolean(document, "snapshot", diagnostics, "facts.deviceOnline.snapshot"));

    private static DeviceControlStateFacts? ReadDeviceControl(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new DeviceControlStateFacts(
            ReadString(document, "control", diagnostics, "facts.deviceControlState.control"),
            ReadString(document, "state", diagnostics, "facts.deviceControlState.state"),
            ReadString(document, "rawState", diagnostics, "facts.deviceControlState.rawState"));

    private static SensorStateFacts? ReadSensor(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new SensorStateFacts(
            ReadString(document, "sensor", diagnostics, "facts.sensorState.sensor"),
            ReadString(document, "state", diagnostics, "facts.sensorState.state"),
            ReadDouble(document, "timeout", diagnostics, "facts.sensorState.timeout"),
            ReadString(document, "timeoutUnit", diagnostics, "facts.sensorState.timeoutUnit"));

    private static ScannerFacts? ReadScanner(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new ScannerFacts(
            ReadInt32(document, "sessionType", diagnostics, "facts.scanner.sessionType"),
            ReadInt32(document, "deviceType", diagnostics, "facts.scanner.deviceType"));

    private static DeviceErrorFacts? ReadDeviceError(BsonDocument? document, ICollection<string> diagnostics) =>
        document is null ? null : new DeviceErrorFacts(
            ReadString(document, "code", diagnostics, "facts.deviceError.code"),
            ReadString(document, "severity", diagnostics, "facts.deviceError.severity"),
            ReadBoolean(document, "retryable", diagnostics, "facts.deviceError.retryable"));

    private static BsonDocument? ReadDocument(
        BsonDocument? parent,
        string name,
        ICollection<string> diagnostics,
        string? path = null)
    {
        if (parent is null || !parent.TryGetValue(name, out var value) || value.IsBsonNull)
        {
            return null;
        }

        if (value is BsonDocument document)
        {
            return document;
        }

        diagnostics.Add($"STAT_FIELD_TYPE:{path ?? name}");
        return null;
    }

    private static string ReadSourceDocumentId(BsonDocument document, ICollection<string> diagnostics)
    {
        if (!document.TryGetValue("_id", out var value) || value.IsBsonNull)
        {
            diagnostics.Add("STAT_SOURCE_DOCUMENT_ID_MISSING");
            return string.Empty;
        }

        return value switch
        {
            BsonObjectId objectId => objectId.Value.ToString(),
            BsonString stringValue when !string.IsNullOrWhiteSpace(stringValue.Value) => stringValue.Value,
            _ => UnsupportedSourceId(value, diagnostics)
        };
    }

    private static string UnsupportedSourceId(BsonValue value, ICollection<string> diagnostics)
    {
        diagnostics.Add("STAT_SOURCE_DOCUMENT_ID_UNSUPPORTED");
        return $"foreign:{value.BsonType}:{value}";
    }

    private static string? ReadString(
        BsonDocument? document,
        string name,
        ICollection<string> diagnostics,
        string? path = null)
    {
        if (document is null || !document.TryGetValue(name, out var value) || value.IsBsonNull)
        {
            return null;
        }

        if (value is BsonString stringValue)
        {
            return stringValue.Value;
        }

        diagnostics.Add($"STAT_FIELD_TYPE:{path ?? name}");
        return null;
    }

    private static long? ReadInt64(
        BsonDocument? document,
        string name,
        ICollection<string> diagnostics,
        string? path = null)
    {
        if (document is null || !document.TryGetValue(name, out var value) || value.IsBsonNull)
        {
            return null;
        }

        try
        {
            return value.BsonType switch
            {
                BsonType.Int32 => value.AsInt32,
                BsonType.Int64 => value.AsInt64,
                BsonType.Double => checked((long)value.AsDouble),
                BsonType.Decimal128 => checked((long)Decimal128.ToDecimal(value.AsDecimal128)),
                BsonType.String when long.TryParse(value.AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => InvalidNumeric(value, diagnostics, path ?? name)
            };
        }
        catch (OverflowException)
        {
            diagnostics.Add($"STAT_FIELD_RANGE:{path ?? name}");
            return null;
        }
    }

    private static int? ReadInt32(
        BsonDocument? document,
        string name,
        ICollection<string> diagnostics,
        string? path = null)
    {
        var value = ReadInt64(document, name, diagnostics, path);
        if (value is null || value < int.MinValue || value > int.MaxValue)
        {
            if (value is not null) diagnostics.Add($"STAT_FIELD_RANGE:{path ?? name}");
            return null;
        }

        return (int)value;
    }

    private static double? ReadDouble(
        BsonDocument? document,
        string name,
        ICollection<string> diagnostics,
        string? path = null)
    {
        if (document is null || !document.TryGetValue(name, out var value) || value.IsBsonNull)
        {
            return null;
        }

        try
        {
            return value.BsonType switch
            {
                BsonType.Double => value.AsDouble,
                BsonType.Int32 => value.AsInt32,
                BsonType.Int64 => value.AsInt64,
                BsonType.Decimal128 => (double)value.AsDecimal128,
                BsonType.String when double.TryParse(value.AsString, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => InvalidDouble(value, diagnostics, path ?? name)
            };
        }
        catch (OverflowException)
        {
            diagnostics.Add($"STAT_FIELD_RANGE:{path ?? name}");
            return null;
        }
    }

    private static bool? ReadBoolean(
        BsonDocument? document,
        string name,
        ICollection<string> diagnostics,
        string? path = null)
    {
        if (document is null || !document.TryGetValue(name, out var value) || value.IsBsonNull)
        {
            return null;
        }

        if (value is BsonBoolean booleanValue)
        {
            return booleanValue.Value;
        }

        if (value.BsonType == BsonType.String &&
            bool.TryParse(value.AsString, out var parsed))
        {
            return parsed;
        }

        diagnostics.Add($"STAT_FIELD_TYPE:{path ?? name}");
        return null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(
        BsonDocument? document,
        string name,
        ICollection<string> diagnostics,
        string? path = null)
    {
        if (document is null || !document.TryGetValue(name, out var value) || value.IsBsonNull)
        {
            return null;
        }

        if (value is BsonDateTime dateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime.ToUniversalTime(), DateTimeKind.Utc));
        }

        if (value is BsonString stringValue &&
            DateTimeOffset.TryParse(
                stringValue.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        diagnostics.Add($"STAT_FIELD_TYPE:{path ?? name}");
        return null;
    }

    private static long? InvalidNumeric(BsonValue value, ICollection<string> diagnostics, string path)
    {
        diagnostics.Add($"STAT_FIELD_TYPE:{path}");
        return null;
    }

    private static double? InvalidDouble(BsonValue value, ICollection<string> diagnostics, string path)
    {
        diagnostics.Add($"STAT_FIELD_TYPE:{path}");
        return null;
    }
}
