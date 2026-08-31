using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.UnitTests;

public sealed class CanonicalModelV2Tests
{
    [Fact]
    public void Canonical_event_supports_non_file_source_without_fake_file_context()
    {
        var receivedAtUtc = new DateTimeOffset(2026, 8, 28, 8, 30, 0, TimeSpan.Zero);
        var deviceEvent = new CanonicalDeviceEvent
        {
            EventId = "event-v2",
            SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
            Category = AppConst.Categories.DeviceConnection,
            SourceKind = AppConst.SourceKinds.ErpAppHub,
            CompanyId = 2,
            ReceivedAtUtc = receivedAtUtc,
            TimelineAtUtc = receivedAtUtc,
            TimeBasis = AppConst.TimeBases.Received,
            Source = new CanonicalDeviceEvent.SourceContext
            {
                Producer = "ERP.AppHub",
                SourceId = "erp-apphub-ua",
                Transport = AppConst.SourceTransports.ClassicSignalR,
                EventName = "receiveStateConnected",
                DeliveryKind = AppConst.DeliveryKinds.Realtime,
                ConnectionGeneration = "generation-1",
                ReceiveSequence = 42
            },
            RawPayload = new CanonicalDeviceEvent.RawPayloadContext
            {
                Format = "signalr-arguments-json-v1",
                ArgumentsJson = "[{\"DeviceId\":101}]",
                Sha256 = "payload-hash",
                SizeBytes = 18
            },
            Facts = new CanonicalDeviceEvent.FactsContext
            {
                Connection = new CanonicalDeviceEvent.ConnectionFacts
                {
                    Status = "connected"
                }
            },
            Parse = new CanonicalDeviceEvent.ParseContext
            {
                Status = AppConst.Parsing.StatusParsed,
                ParserVersion = "erp-apphub-v1"
            }
        };

        Assert.Null(deviceEvent.Source.FileId);
        Assert.Null(deviceEvent.Source.RelativePath);
        Assert.Equal(AppConst.SourceTransports.ClassicSignalR, deviceEvent.Source.Transport);
        Assert.Equal(AppConst.DeliveryKinds.Realtime, deviceEvent.Source.DeliveryKind);
        Assert.Equal(receivedAtUtc, deviceEvent.TimelineAtUtc);
        Assert.Equal(AppConst.TimeBases.Received, deviceEvent.TimeBasis);
        Assert.Equal("[{\"DeviceId\":101}]", deviceEvent.RawPayload.ArgumentsJson);
        Assert.NotNull(deviceEvent.Facts.Connection);
        Assert.Null(deviceEvent.Facts.TagRead);
    }

    [Fact]
    public void Canonical_failure_is_source_neutral_and_can_omit_file_context()
    {
        var failure = new CanonicalIngestionFailure
        {
            FailureId = "failure-v2",
            SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
            SourceKind = AppConst.SourceKinds.ErpAppHub,
            Source = new CanonicalDeviceEvent.SourceContext
            {
                Producer = "ERP.AppHub",
                SourceId = "erp-apphub-ua",
                Transport = AppConst.SourceTransports.ClassicSignalR,
                EventName = "receiveDeviceReadTag",
                DeliveryKind = AppConst.DeliveryKinds.Realtime,
                ConnectionGeneration = "generation-1",
                ReceiveSequence = 7
            },
            RawPayload = new CanonicalDeviceEvent.RawPayloadContext
            {
                Format = "signalr-arguments-json-v1",
                ArgumentsJson = "[]",
                Sha256 = "payload-hash",
                SizeBytes = 2
            },
            Error = new CanonicalIngestionFailure.ErrorContext
            {
                Code = AppConst.Parsing.TenantUnresolved,
                Message = "CompanyId cannot be resolved.",
                Stage = AppConst.IngestionStages.MetadataResolution,
                ParserVersion = "erp-apphub-v1"
            }
        };

        Assert.Null(failure.CompanyId);
        Assert.Null(failure.Source.FileId);
        Assert.Equal(AppConst.Parsing.TenantUnresolved, failure.Error.Code);
        Assert.Equal(AppConst.IngestionStages.MetadataResolution, failure.Error.Stage);
    }
}
