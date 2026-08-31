using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.UnitTests;

public sealed class MongoPersistenceCoordinatorTests
{
    [Fact]
    public async Task Persists_event_before_advancing_checkpoint()
    {
        var calls = new List<string>();
        var historyWriter = new RecordingHistoryWriter(calls);
        var checkpointStore = new RecordingCheckpointStore(calls, CheckpointAdvanceStatus.Advanced);
        var persistenceService = new CanonicalIngestionPersistenceService(
            historyWriter,
            new NoopFailureWriter(),
            TimeProvider.System);
        var coordinator = new RawRecordPersistenceCoordinator(
            persistenceService,
            checkpointStore,
            TimeProvider.System);

        var result = await coordinator.PersistAsync(
            new RawRecordProcessingResult { Event = CreateEvent() },
            CreateCheckpoint(),
            observedFileLength: 500,
            workerId: "unit-test-worker",
            CancellationToken.None);

        Assert.Equal(["history", "checkpoint"], calls);
        Assert.True(result.IsConfirmed);
        Assert.False(result.WasFailure);
    }

    [Fact]
    public async Task Does_not_report_confirmed_when_checkpoint_cas_conflicts()
    {
        var checkpointStore = new RecordingCheckpointStore([], CheckpointAdvanceStatus.Conflict);
        var persistenceService = new CanonicalIngestionPersistenceService(
            new RecordingHistoryWriter([]),
            new NoopFailureWriter(),
            TimeProvider.System);
        var coordinator = new RawRecordPersistenceCoordinator(
            persistenceService,
            checkpointStore,
            TimeProvider.System);

        var result = await coordinator.PersistAsync(
            new RawRecordProcessingResult { Event = CreateEvent() },
            CreateCheckpoint(),
            observedFileLength: null,
            workerId: "unit-test-worker",
            CancellationToken.None);

        Assert.False(result.IsConfirmed);
        Assert.Equal(CheckpointAdvanceStatus.Conflict, result.CheckpointResult.Status);
    }

    private static IngestionCheckpoint CreateCheckpoint() => new()
    {
        Key = new IngestionCheckpointKey
        {
            SourceId = "unit-test-source",
            FolderDate = new DateOnly(2026, 8, 25),
            FileId = 1,
            RelativePath = "2026/08/25/File_1.txt"
        },
        Position = 0,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        Version = 0
    };

    private static CanonicalDeviceEvent CreateEvent() => new()
    {
        EventId = "unit-event-id",
        SchemaVersion = AppConst.RawLog.SchemaVersion,
        Category = AppConst.Categories.TagRead,
        SourceKind = AppConst.RawLog.SourceKind,
        CompanyId = 2,
        Source = new CanonicalDeviceEvent.SourceContext
        {
            Producer = AppConst.RawLog.Producer,
            SourceId = "unit-test-source",
            FileId = 1,
            FileName = "File_1.txt",
            RelativePath = "2026/08/25/File_1.txt",
            FolderDate = new DateOnly(2026, 8, 25),
            OffsetStart = 100,
            OffsetEnd = 200
        },
        RawPayload = new CanonicalDeviceEvent.RawPayloadContext
        {
            Format = AppConst.RawLog.PayloadFormat,
            Text = "@(TAG001,14:00:00,101,5)e(0)",
            Sha256 = "unit-hash"
        },
        Facts = new CanonicalDeviceEvent.FactsContext(),
        Parse = new CanonicalDeviceEvent.ParseContext
        {
            Status = AppConst.Parsing.StatusParsed,
            ParserVersion = AppConst.RawLog.ParserVersion
        }
    };

    private sealed class RecordingHistoryWriter(List<string> calls) : IDeviceEventHistoryWriter
    {
        public Task<PersistenceWriteResult> WriteAsync(
            CanonicalDeviceEvent deviceEvent,
            DateTimeOffset receivedAtUtc,
            string workerId,
            CancellationToken cancellationToken)
        {
            calls.Add("history");
            return Task.FromResult(new PersistenceWriteResult(deviceEvent.EventId, false));
        }
    }

    private sealed class NoopFailureWriter : IIngestionFailureWriter
    {
        public Task<PersistenceWriteResult> WriteAsync(
            CanonicalIngestionFailure failure,
            DateTimeOffset receivedAtUtc,
            string workerId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Failure writer should not be called for an event.");
    }

    private sealed class RecordingCheckpointStore(
        List<string> calls,
        CheckpointAdvanceStatus status) : IIngestionCheckpointStore
    {
        public Task<IngestionCheckpoint?> LoadAsync(
            IngestionCheckpointKey key,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestionCheckpoint?>(null);

        public Task<CheckpointAdvanceResult> AdvanceAsync(
            IngestionCheckpointKey key,
            long expectedVersion,
            CheckpointAdvanceRequest request,
            CancellationToken cancellationToken)
        {
            calls.Add("checkpoint");
            return Task.FromResult(new CheckpointAdvanceResult(status, null));
        }
    }
}
