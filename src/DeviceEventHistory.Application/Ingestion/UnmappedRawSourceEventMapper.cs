using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.Application.Ingestion;

public sealed class UnmappedRawSourceEventMapper : IRawSourceEventMapper
{
    public string SourceKind => string.Empty;

    public string EventName => string.Empty;

    public CanonicalIngestionResult Map(RawSourceEvent sourceEvent)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);

        var message = AppConst.Messages.Format(
            AppConst.Messages.MSG_RAW_SOURCE_EVENT_UNMAPPED,
            sourceEvent.SourceKind,
            sourceEvent.EventName);

        return CanonicalIngestionResult.FromFailure(new CanonicalIngestionFailure
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
                SizeBytes = System.Text.Encoding.UTF8.GetByteCount(sourceEvent.RawArgumentsJson)
            },
            Error = new CanonicalIngestionFailure.ErrorContext
            {
                Code = AppConst.Parsing.UnknownSourceEvent,
                Message = message,
                Stage = AppConst.IngestionStages.Mapping,
                ParserVersion = AppConst.AppHub.ParserVersion
            },
            ReceivedAtUtc = sourceEvent.ReceivedAtUtc,
            Retryable = false
        });
    }
}
