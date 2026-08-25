using System.Text;
using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;
using DeviceEventHistory.Worker.Configuration;
using DeviceEventHistory.Worker.Orchestration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.UnitTests;

public sealed class WorkerOrchestrationTests
{
    [Fact]
    public async Task Partial_record_waits_then_persists_once_after_next_chunk()
    {
        const string record = "@(TAG001,08:00:00,101,5)e(0)";
        var firstChunk = Encoding.UTF8.GetBytes(record[..^4]);
        var secondChunk = Encoding.UTF8.GetBytes(record[^4..]);
        var tailReader = new SequenceTailReader(
            _ => new RawLogTailReadResult
            {
                StartOffset = 0,
                NextOffset = firstChunk.Length,
                FileLength = firstChunk.Length,
                Data = firstChunk,
                IsTruncated = false
            },
            offset => offset == firstChunk.Length
                ? new RawLogTailReadResult
                {
                    StartOffset = offset,
                    NextOffset = record.Length,
                    FileLength = record.Length,
                    Data = secondChunk,
                    IsTruncated = false
                }
                : EmptyRead(offset));
        var persistence = new RecordingPersistenceCoordinator();
        var state = CreateState(record.Length);
        var processor = CreateProcessor(tailReader, persistence, maxRecordsPerTurn: 10);

        var first = await processor.ProcessAsync(state, CancellationToken.None);
        var second = await processor.ProcessAsync(state, CancellationToken.None);

        Assert.Equal(FileTurnStatus.WaitingForMoreData, first.Status);
        Assert.Equal(FileTurnStatus.CaughtUp, second.Status);
        Assert.Single(persistence.PersistedOffsets);
        Assert.Equal(record.Length, state.Checkpoint.Position);
    }

    [Fact]
    public async Task Max_records_per_turn_preserves_ready_records_for_fair_requeue()
    {
        const string firstRecord = "@(TAG001,08:00:00,101,5)e(0)";
        const string secondRecord = "@(TAG002,08:00:01,101,5)e(0)";
        var bytes = Encoding.UTF8.GetBytes(firstRecord + secondRecord);
        var tailReader = new SequenceTailReader(offset => offset == 0
            ? new RawLogTailReadResult
            {
                StartOffset = 0,
                NextOffset = bytes.Length,
                FileLength = bytes.Length,
                Data = bytes,
                IsTruncated = false
            }
            : EmptyRead(offset));
        var persistence = new RecordingPersistenceCoordinator();
        var state = CreateState(bytes.Length);
        var processor = CreateProcessor(tailReader, persistence, maxRecordsPerTurn: 1);

        var firstTurn = await processor.ProcessAsync(state, CancellationToken.None);
        var secondTurn = await processor.ProcessAsync(state, CancellationToken.None);
        var thirdTurn = await processor.ProcessAsync(state, CancellationToken.None);

        Assert.Equal(FileTurnStatus.Requeue, firstTurn.Status);
        Assert.Equal(FileTurnStatus.Requeue, secondTurn.Status);
        Assert.Equal(FileTurnStatus.CaughtUp, thirdTurn.Status);
        Assert.Equal(2, persistence.PersistedOffsets.Count);
        Assert.Equal(firstRecord.Length, persistence.PersistedOffsets[0]);
        Assert.Equal(bytes.Length, persistence.PersistedOffsets[1]);
    }

    [Fact]
    public async Task Restart_uses_checkpoint_position_and_does_not_reprocess_committed_record()
    {
        const string record = "@(TAG001,08:00:00,101,5)e(0)";
        var bytes = Encoding.UTF8.GetBytes(record);
        var tailReader = new SequenceTailReader(_ => EmptyRead(bytes.Length));
        var persistence = new RecordingPersistenceCoordinator();
        var state = CreateState(bytes.Length, checkpointPosition: bytes.Length);
        var processor = CreateProcessor(tailReader, persistence, maxRecordsPerTurn: 10);

        var result = await processor.ProcessAsync(state, CancellationToken.None);

        Assert.Equal(FileTurnStatus.CaughtUp, result.Status);
        Assert.Empty(persistence.PersistedOffsets);
        Assert.Equal(bytes.Length, state.ReadOffset);
    }

    [Fact]
    public async Task Truncated_file_stops_the_turn_without_advancing_checkpoint()
    {
        var tailReader = new SequenceTailReader(_ => new RawLogTailReadResult
        {
            StartOffset = 100,
            NextOffset = 100,
            FileLength = 50,
            Data = [],
            IsTruncated = true
        });
        var persistence = new RecordingPersistenceCoordinator();
        var state = CreateState(100, checkpointPosition: 100);
        var processor = CreateProcessor(tailReader, persistence, maxRecordsPerTurn: 10);

        var result = await processor.ProcessAsync(state, CancellationToken.None);

        Assert.Equal(FileTurnStatus.Truncated, result.Status);
        Assert.Equal(100, state.Checkpoint.Position);
        Assert.Empty(persistence.PersistedOffsets);
    }

    private static FileTurnProcessor CreateProcessor(
        IRawLogTailReader tailReader,
        RecordingPersistenceCoordinator persistence,
        int maxRecordsPerTurn) =>
        new(
            tailReader,
            new FakeRecordHandler(),
            persistence,
            new EmptyCheckpointStore(),
            Options.Create(new RfidRawLogOptions
            {
                ReadBufferBytes = 1024,
                MaxRecordBytes = 4096,
                MaxBytesPerTurn = 4096,
                MaxRecordsPerTurn = maxRecordsPerTurn,
                MaxTurnDuration = TimeSpan.FromMinutes(1)
            }),
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "unit-test-worker" }),
            TimeProvider.System);

    private static FileIngestionState CreateState(
        int fileLength,
        long checkpointPosition = 0)
    {
        var descriptor = new RawLogFileDescriptor
        {
            SourceId = "unit-test-source",
            CompanyId = 2,
            Mode = RawLogSourceMode.Local,
            TimeZoneId = "UTC",
            FolderDate = new DateOnly(2026, 8, 25),
            FileId = 1,
            FileName = "File_1.txt",
            Location = "D:/raw/File_1.txt",
            RelativePath = "2026/08/25/File_1.txt",
            Length = fileLength
        };
        var key = new IngestionCheckpointKey
        {
            SourceId = descriptor.SourceId,
            FolderDate = descriptor.FolderDate,
            FileId = descriptor.FileId,
            RelativePath = descriptor.RelativePath
        };
        var checkpoint = new IngestionCheckpoint
        {
            Key = key,
            Position = checkpointPosition,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Version = checkpointPosition == 0 ? 0 : 1
        };

        return new FileIngestionState(
            descriptor,
            checkpoint,
            checkpointPosition,
            new RawLogRecordFramer(4096),
            startupExistingFile: false);
    }

    private static RawLogTailReadResult EmptyRead(long offset) => new()
    {
        StartOffset = offset,
        NextOffset = offset,
        FileLength = offset,
        Data = [],
        IsTruncated = false
    };

    private sealed class SequenceTailReader(params Func<long, RawLogTailReadResult>[] reads) : IRawLogTailReader
    {
        private int readIndex;

        public Task<RawLogTailReadResult> ReadAsync(
            RawLogFileDescriptor file,
            long offset,
            CancellationToken cancellationToken = default)
        {
            var index = Math.Min(readIndex++, reads.Length - 1);
            return Task.FromResult(reads[index](offset));
        }
    }

    private sealed class FakeRecordHandler : IProcessRawFileRecordHandler
    {
        public RawRecordProcessingResult Handle(RawRecordContext context) => new()
        {
            Event = new CanonicalDeviceEvent
            {
                EventId = $"event-{context.OffsetStart}",
                SchemaVersion = AppConst.RawLog.SchemaVersion,
                Category = AppConst.Categories.Unknown,
                SourceKind = AppConst.RawLog.SourceKind,
                CompanyId = context.CompanyId,
                Source = new CanonicalDeviceEvent.SourceContext
                {
                    Producer = AppConst.RawLog.Producer,
                    SourceId = context.SourceId,
                    FileId = context.FileId,
                    FileName = context.FileName,
                    RelativePath = context.RelativePath,
                    FolderDate = context.FolderDate,
                    OffsetStart = context.OffsetStart,
                    OffsetEnd = context.OffsetEnd
                },
                RawPayload = new CanonicalDeviceEvent.RawPayloadContext
                {
                    Format = AppConst.RawLog.PayloadFormat,
                    Text = context.RawPayloadText,
                    Sha256 = EventIdentityFactory.ComputePayloadHash(context)
                },
                Facts = new CanonicalDeviceEvent.FactsContext(),
                Parse = new CanonicalDeviceEvent.ParseContext
                {
                    Status = AppConst.Parsing.StatusParsed,
                    ParserVersion = AppConst.RawLog.ParserVersion
                }
            }
        };
    }

    private sealed class RecordingPersistenceCoordinator : IRawRecordPersistenceCoordinator
    {
        public List<long> PersistedOffsets { get; } = [];

        public Task<RawRecordPersistenceOutcome> PersistAsync(
            RawRecordProcessingResult processingResult,
            IngestionCheckpoint checkpoint,
            long? observedFileLength,
            string workerId,
            CancellationToken cancellationToken)
        {
            var deviceEvent = processingResult.Event!;
            PersistedOffsets.Add(deviceEvent.Source.OffsetEnd);
            var updatedCheckpoint = checkpoint with
            {
                Position = deviceEvent.Source.OffsetEnd,
                Version = checkpoint.Version + 1,
                LastEventId = deviceEvent.EventId,
                LastRecordHash = deviceEvent.RawPayload.Sha256,
                ObservedFileLength = observedFileLength,
                WorkerId = workerId,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return Task.FromResult(new RawRecordPersistenceOutcome(
                deviceEvent.EventId,
                WasFailure: false,
                WasAlreadyPersisted: false,
                new CheckpointAdvanceResult(
                    CheckpointAdvanceStatus.Advanced,
                    updatedCheckpoint)));
        }
    }

    private sealed class EmptyCheckpointStore : IIngestionCheckpointStore
    {
        public Task<IngestionCheckpoint?> LoadAsync(
            IngestionCheckpointKey key,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestionCheckpoint?>(null);

        public Task<CheckpointAdvanceResult> AdvanceAsync(
            IngestionCheckpointKey key,
            long expectedVersion,
            CheckpointAdvanceRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
