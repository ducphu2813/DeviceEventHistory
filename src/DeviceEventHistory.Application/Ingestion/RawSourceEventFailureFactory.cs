using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.Application.Ingestion;

public static class RawSourceEventFailureFactory
{
    public static CanonicalIngestionResult CreatePayloadTooLargeFailure(
        RawSourceEvent sourceEvent,
        long maximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);

        return CanonicalIngestionResult.FromFailure(new CanonicalIngestionFailure
        {
            FailureId = RawSourceEventIdentityFactory.CreateFailureId(sourceEvent),
            SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
            SourceKind = sourceEvent.SourceKind,
            Source = CreateSourceContext(sourceEvent),
            RawPayload = new CanonicalDeviceEvent.RawPayloadContext
            {
                Format = AppConst.AppHub.PayloadFormat,
                Sha256 = sourceEvent.PayloadSha256,
                SizeBytes = sourceEvent.PayloadSizeBytes
            },
            Error = new CanonicalIngestionFailure.ErrorContext
            {
                Code = AppConst.Parsing.PayloadTooLarge,
                Message = AppConst.Messages.Format(
                    AppConst.Messages.MSG_APPHUB_PAYLOAD_TOO_LARGE,
                    sourceEvent.PayloadSizeBytes,
                    maximumPayloadBytes),
                Stage = AppConst.IngestionStages.Admission,
                ParserVersion = AppConst.AppHub.ParserVersion,
                Details =
                [
                    $"{AppConst.Parsing.PayloadSizeBytesDetail}={sourceEvent.PayloadSizeBytes}",
                    $"{AppConst.Parsing.MaximumPayloadBytesDetail}={maximumPayloadBytes}"
                ]
            },
            ReceivedAtUtc = sourceEvent.ReceivedAtUtc,
            Retryable = false
        });
    }

    private static CanonicalDeviceEvent.SourceContext CreateSourceContext(
        RawSourceEvent sourceEvent) =>
        new()
        {
            Producer = sourceEvent.SourceApplication,
            SourceId = sourceEvent.SourceId,
            Transport = sourceEvent.SourceTransport,
            EventName = sourceEvent.EventName,
            DeliveryKind = sourceEvent.DeliveryKind,
            ConnectionGeneration = sourceEvent.ConnectionGeneration,
            ReceiveSequence = sourceEvent.ReceiveSequence
        };
}
