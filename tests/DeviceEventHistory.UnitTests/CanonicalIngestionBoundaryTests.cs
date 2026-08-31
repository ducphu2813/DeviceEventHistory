using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.UnitTests;

public sealed class CanonicalIngestionBoundaryTests
{
    [Fact]
    public async Task Persistence_service_enriches_and_writes_event_without_checkpoint_dependency()
    {
        var historyWriter = new RecordingHistoryWriter();
        var service = new CanonicalIngestionPersistenceService(
            historyWriter,
            new RecordingFailureWriter(),
            TimeProvider.System);
        var receivedAtUtc = new DateTimeOffset(2026, 8, 28, 8, 30, 0, TimeSpan.Zero);

        var result = await service.PersistAsync(
            CanonicalIngestionResult.FromEvent(CreateEvent(receivedAtUtc)),
            "unit-test-worker",
            CancellationToken.None);

        Assert.Equal("event-id", result.PersistedIdentity);
        Assert.False(result.WasFailure);
        Assert.Equal(receivedAtUtc, historyWriter.Event!.ReceivedAtUtc);
        Assert.NotNull(historyWriter.Event.PersistedAtUtc);
        Assert.Equal(AppConst.TimeBases.Occurred, historyWriter.Event.TimeBasis);
        Assert.Equal("unit-test-worker", historyWriter.Event.Ingestion!.WorkerId);
        Assert.NotNull(historyWriter.Event.Ingestion.ProcessingDurationMs);
    }

    [Fact]
    public async Task Persistence_service_writes_failure_as_a_source_neutral_outcome()
    {
        var failureWriter = new RecordingFailureWriter();
        var service = new CanonicalIngestionPersistenceService(
            new RecordingHistoryWriter(),
            failureWriter,
            TimeProvider.System);

        var result = await service.PersistAsync(
            CanonicalIngestionResult.FromFailure(CreateFailure()),
            "unit-test-worker",
            CancellationToken.None);

        Assert.Equal("failure-id", result.PersistedIdentity);
        Assert.True(result.WasFailure);
        Assert.Equal("unit-test-worker", failureWriter.Failure!.Ingestion!.WorkerId);
        Assert.NotNull(failureWriter.Failure.PersistedAtUtc);
    }

    [Fact]
    public async Task Persistence_service_rejects_zero_or_multiple_outcomes()
    {
        var service = new CanonicalIngestionPersistenceService(
            new RecordingHistoryWriter(),
            new RecordingFailureWriter(),
            TimeProvider.System);

        var noOutcome = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PersistAsync(
            new CanonicalIngestionResult(),
            "unit-test-worker",
            CancellationToken.None));
        Assert.Equal(AppConst.Messages.MSG_CANONICAL_INGESTION_OUTCOME_REQUIRED, noOutcome.Message);

        var bothOutcomes = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PersistAsync(
            new CanonicalIngestionResult
            {
                Event = CreateEvent(null),
                Failure = CreateFailure()
            },
            "unit-test-worker",
            CancellationToken.None));
        Assert.Equal(AppConst.Messages.MSG_CANONICAL_INGESTION_OUTCOME_EXCLUSIVE, bothOutcomes.Message);
    }

    [Fact]
    public void Mapper_registry_dispatches_exact_key_and_rejects_duplicates()
    {
        var mapper = new StubMapper(AppConst.SourceKinds.ErpAppHub, "receiveDeviceOnline");
        var registry = new RawSourceEventMapperRegistry(
            [mapper],
            new UnmappedRawSourceEventMapper());

        var result = registry.Map(CreateSourceEvent("receiveDeviceOnline"));

        Assert.Same(mapper.Result, result);
        var exception = Assert.Throws<InvalidOperationException>(() => new RawSourceEventMapperRegistry(
            [mapper, new StubMapper(mapper.SourceKind, mapper.EventName)],
            new UnmappedRawSourceEventMapper()));
        Assert.Equal(
            AppConst.Messages.Format(
                AppConst.Messages.MSG_RAW_SOURCE_EVENT_MAPPER_KEY_DUPLICATED,
                RawSourceEventMapperRegistry.CreateKey(mapper.SourceKind, mapper.EventName)),
            exception.Message);
    }

    [Fact]
    public void Unknown_mapper_key_uses_explicit_unmapped_fallback()
    {
        var registry = new RawSourceEventMapperRegistry([], new UnmappedRawSourceEventMapper());

        var result = registry.Map(CreateSourceEvent("unknownCallback"));

        Assert.Null(result.Event);
        Assert.Equal(AppConst.Parsing.UnknownSourceEvent, result.Failure!.Error.Code);
        Assert.Equal(AppConst.SchemaVersions.CanonicalV2, result.Failure.SchemaVersion);
        Assert.Equal("unknownCallback", result.Failure.Source.EventName);
    }

    private static CanonicalDeviceEvent CreateEvent(DateTimeOffset? receivedAtUtc) => new()
    {
        EventId = "event-id",
        SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
        Category = AppConst.Categories.DeviceOnline,
        SourceKind = AppConst.SourceKinds.ErpAppHub,
        CompanyId = 2,
        OccurredAtUtc = new DateTimeOffset(2026, 8, 28, 8, 29, 0, TimeSpan.Zero),
        ReceivedAtUtc = receivedAtUtc,
        Source = new CanonicalDeviceEvent.SourceContext
        {
            Producer = "ERP.AppHub",
            SourceId = "apphub-source",
            Transport = AppConst.SourceTransports.ClassicSignalR,
            EventName = "receiveDeviceOnline",
            DeliveryKind = AppConst.DeliveryKinds.Realtime,
            ConnectionGeneration = "generation-1",
            ReceiveSequence = 1
        },
        RawPayload = new CanonicalDeviceEvent.RawPayloadContext
        {
            Format = AppConst.AppHub.PayloadFormat,
            ArgumentsJson = "[{\"DeviceId\":101}]",
            Sha256 = "payload-hash",
            SizeBytes = 18
        },
        Facts = new CanonicalDeviceEvent.FactsContext(),
        Parse = new CanonicalDeviceEvent.ParseContext
        {
            Status = AppConst.Parsing.StatusParsed,
            ParserVersion = AppConst.AppHub.ParserVersion
        }
    };

    private static CanonicalIngestionFailure CreateFailure() => new()
    {
        FailureId = "failure-id",
        SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
        SourceKind = AppConst.SourceKinds.ErpAppHub,
        Source = CreateEvent(null).Source,
        RawPayload = CreateEvent(null).RawPayload,
        Error = new CanonicalIngestionFailure.ErrorContext
        {
            Code = AppConst.Parsing.TenantUnresolved,
            Message = "tenant unresolved",
            Stage = AppConst.IngestionStages.MetadataResolution,
            ParserVersion = AppConst.AppHub.ParserVersion
        },
        ReceivedAtUtc = new DateTimeOffset(2026, 8, 28, 8, 30, 0, TimeSpan.Zero)
    };

    private static RawSourceEvent CreateSourceEvent(string eventName) => new()
    {
        IngestionEventId = "ingestion-id",
        SourceKind = AppConst.SourceKinds.ErpAppHub,
        SourceId = "apphub-source",
        SourceApplication = "ERP.AppHub",
        SourceTransport = AppConst.SourceTransports.ClassicSignalR,
        EventName = eventName,
        ReceivedAtUtc = new DateTimeOffset(2026, 8, 28, 8, 30, 0, TimeSpan.Zero),
        RawArgumentsJson = "[]",
        PayloadSha256 = "payload-hash",
        PayloadSizeBytes = 2,
        ConnectionGeneration = "generation-1",
        ReceiveSequence = 1,
        DeliveryKind = AppConst.DeliveryKinds.Realtime
    };

    private sealed class StubMapper(string sourceKind, string eventName) : IRawSourceEventMapper
    {
        public string SourceKind { get; } = sourceKind;

        public string EventName { get; } = eventName;

        public CanonicalIngestionResult Result { get; } = CanonicalIngestionResult.FromFailure(CreateFailure());

        public CanonicalIngestionResult Map(RawSourceEvent sourceEvent) => Result;
    }

    private sealed class RecordingHistoryWriter : IDeviceEventHistoryWriter
    {
        public CanonicalDeviceEvent? Event { get; private set; }

        public Task<PersistenceWriteResult> WriteAsync(
            CanonicalDeviceEvent deviceEvent,
            DateTimeOffset receivedAtUtc,
            string workerId,
            CancellationToken cancellationToken)
        {
            Event = deviceEvent;
            return Task.FromResult(new PersistenceWriteResult(deviceEvent.EventId, false));
        }
    }

    private sealed class RecordingFailureWriter : IIngestionFailureWriter
    {
        public CanonicalIngestionFailure? Failure { get; private set; }

        public Task<PersistenceWriteResult> WriteAsync(
            CanonicalIngestionFailure failure,
            DateTimeOffset receivedAtUtc,
            string workerId,
            CancellationToken cancellationToken)
        {
            Failure = failure;
            return Task.FromResult(new PersistenceWriteResult(failure.FailureId, false));
        }
    }
}
