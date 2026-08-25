using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Common;
using MongoDB.Bson;

namespace DeviceEventHistory.Infrastructure.MongoDb.Mapping;

internal static class CanonicalDeviceEventDocumentMapper
{
    public static BsonDocument ToDocument(
        CanonicalDeviceEvent deviceEvent,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset persistedAtUtc,
        string workerId) => new()
    {
        { "_id", ObjectId.GenerateNewId() },
        { "eventId", deviceEvent.EventId },
        { "schemaVersion", deviceEvent.SchemaVersion },
        { "category", deviceEvent.Category },
        { "sourceKind", deviceEvent.SourceKind },
        { "companyId", deviceEvent.CompanyId },
        { "occurredAtUtc", MongoDocumentValue.DateTimeOffset(deviceEvent.OccurredAtUtc) },
        { "occurredAtLocal", MongoDocumentValue.String(deviceEvent.OccurredAtLocal?.ToString(AppConst.Identity.IsoDateTimeFormat)) },
        { "receivedAtUtc", MongoDocumentValue.DateTimeOffset(receivedAtUtc) },
        { "persistedAtUtc", MongoDocumentValue.DateTimeOffset(persistedAtUtc) },
        { "source", MapSource(deviceEvent.Source) },
        { "device", MapDevice(deviceEvent.Device) },
        { "rawPayload", MapRawPayload(deviceEvent.RawPayload) },
        { "facts", MapFacts(deviceEvent.Facts) },
        { "parse", MapParse(deviceEvent.Parse) },
        { "ingestion", new BsonDocument("workerId", workerId) }
    };

    private static BsonDocument MapSource(CanonicalDeviceEvent.SourceContext source) => new()
    {
        { "producer", source.Producer },
        { "sourceId", source.SourceId },
        { "fileId", source.FileId },
        { "fileName", source.FileName },
        { "relativePath", source.RelativePath },
        { "folderDate", MongoDocumentValue.DateOnly(source.FolderDate) },
        { "offsetStart", source.OffsetStart },
        { "offsetEnd", source.OffsetEnd }
    };

    private static BsonValue MapDevice(CanonicalDeviceEvent.DeviceContext? device) =>
        device is null
            ? BsonNull.Value
            : new BsonDocument
            {
                { "id", MongoDocumentValue.Int32(device.Id) },
                { "gateId", MongoDocumentValue.Int32(device.GateId) }
            };

    private static BsonDocument MapRawPayload(CanonicalDeviceEvent.RawPayloadContext rawPayload) => new()
    {
        { "format", rawPayload.Format },
        { "text", rawPayload.Text },
        { "sha256", rawPayload.Sha256 }
    };

    private static BsonDocument MapFacts(CanonicalDeviceEvent.FactsContext facts) => new()
    {
        { "tagRead", facts.TagRead is null ? BsonNull.Value : new BsonDocument
            {
                { "tagId", facts.TagRead.TagId },
                { "routingFileId", facts.TagRead.RoutingFileId }
            } },
        { "gateState", facts.GateState is null ? BsonNull.Value : new BsonDocument
            {
                { "stateCode", MongoDocumentValue.Int32(facts.GateState.StateCode) },
                { "rawValue", MongoDocumentValue.String(facts.GateState.RawValue) }
            } },
        { "signal", facts.Signal is null ? BsonNull.Value : new BsonDocument
            {
                { "antennaPort", MongoDocumentValue.Int32(facts.Signal.AntennaPort) },
                { "firstSeenAtLocal", MongoDocumentValue.String(facts.Signal.FirstSeenAtLocal?.ToString(AppConst.Identity.IsoDateTimeFormat)) },
                { "lastSeenAtLocal", MongoDocumentValue.String(facts.Signal.LastSeenAtLocal?.ToString(AppConst.Identity.IsoDateTimeFormat)) },
                { "seenCount", MongoDocumentValue.Int32(facts.Signal.SeenCount) },
                { "txPower", MongoDocumentValue.Int32(facts.Signal.TxPower) },
                { "dopplerFrequency", MongoDocumentValue.Double(facts.Signal.DopplerFrequency) },
                { "phaseAngle", MongoDocumentValue.Double(facts.Signal.PhaseAngle) },
                { "channelMhz", MongoDocumentValue.Double(facts.Signal.ChannelMhz) },
                { "peakRssiDbm", MongoDocumentValue.Double(facts.Signal.PeakRssiDbm) }
            } },
        { "businessEvent", facts.BusinessEvent is null ? BsonNull.Value : new BsonDocument
            {
                { "eventType", MongoDocumentValue.Int32(facts.BusinessEvent.EventType) },
                { "processId", MongoDocumentValue.Int32(facts.BusinessEvent.ProcessId) },
                { "quantity", MongoDocumentValue.Int32(facts.BusinessEvent.Quantity) },
                { "processIdsRaw", MongoDocumentValue.String(facts.BusinessEvent.ProcessIdsRaw) },
                { "processIds", facts.BusinessEvent.ProcessIds is null ? BsonNull.Value : MongoDocumentValue.Int32Array(facts.BusinessEvent.ProcessIds) },
                { "second", MongoDocumentValue.Int32(facts.BusinessEvent.Second) }
            } },
        { "styleProcess", facts.StyleProcess is null ? BsonNull.Value : new BsonDocument
            {
                { "processCustomRaw", MongoDocumentValue.String(facts.StyleProcess.ProcessCustomRaw) },
                { "processCustom", facts.StyleProcess.ProcessCustom is null ? BsonNull.Value : MongoDocumentValue.Int32Array(facts.StyleProcess.ProcessCustom) }
            } },
        { "user", facts.User is null ? BsonNull.Value : new BsonDocument
            {
                { "userId", MongoDocumentValue.Int32(facts.User.UserId) }
            } }
    };

    private static BsonDocument MapParse(CanonicalDeviceEvent.ParseContext parse) => new()
    {
        { "status", parse.Status },
        { "parserVersion", parse.ParserVersion },
        { "warnings", MongoDocumentValue.StringArray(parse.Warnings) },
        { "errors", MongoDocumentValue.StringArray(parse.Errors) }
    };
}
