using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Worker.Configuration;
using DeviceEventHistory.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class FileTurnProcessor(
    IRawLogTailReader tailReader,
    IProcessRawFileRecordHandler recordHandler,
    IRawRecordPersistenceCoordinator persistenceCoordinator,
    IIngestionCheckpointStore checkpointStore,
    IOptions<RfidRawLogOptions> rawLogOptions,
    IOptions<WorkerOptions> workerOptions,
    TimeProvider timeProvider,
    IIngestionTelemetry? ingestionTelemetry = null,
    ILogger<FileTurnProcessor>? recordLogger = null)
{
    private readonly IIngestionTelemetry telemetry =
        ingestionTelemetry ?? NullIngestionTelemetry.Instance;
    private readonly ILogger<FileTurnProcessor> logger =
        recordLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileTurnProcessor>.Instance;

    public async Task<FileTurnResult> ProcessAsync(
        FileIngestionState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsStopped)
        {
            return FileTurnResult.Stopped(
                new InvalidOperationException(AppConst.Messages.MSG_RAW_LOG_STATE_STOPPED));
        }

        var options = rawLogOptions.Value;
        var startedAt = timeProvider.GetTimestamp();
        var bytesReadThisTurn = 0;
        var recordsProcessedThisTurn = 0;
        var lastReadHadMoreData = false;

        while (HasBudget(startedAt, bytesReadThisTurn, recordsProcessedThisTurn, options, timeProvider))
        {
            if (state.ReadyRecordCount == 0)
            {
                var read = await tailReader.ReadAsync(
                    state.Descriptor,
                    state.ReadOffset,
                    cancellationToken);
                state.SetLastObservedFileLength(read.FileLength);
                lastReadHadMoreData = read.HasMore;
                if (read.Data.Length > 0 || read.IsTruncated || read.HasMore || state.HasPendingBytes)
                {
                    logger.LogDebug(
                        AppConst.Logging.FileTurnReadMessage,
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId,
                        state.ReadOffset,
                        read.Data.Length,
                        read.NextOffset,
                        read.FileLength,
                        read.HasMore,
                        state.Framer.PendingByteCount);
                }
                else
                {
                    logger.LogTrace(
                        AppConst.Logging.FileTurnReadMessage,
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId,
                        state.ReadOffset,
                        read.Data.Length,
                        read.NextOffset,
                        read.FileLength,
                        read.HasMore,
                        state.Framer.PendingByteCount);
                }

                if (read.IsTruncated)
                {
                    state.ResetToCheckpoint();
                    telemetry.RecordFileTruncated(
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId);
                    return FileTurnResult.Truncated();
                }

                telemetry.RecordBytesRead(
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    read.Data.Length);
                if (read.Data.Length == 0)
                {
                    telemetry.RecordProgress(
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId,
                        state.Checkpoint.Position,
                        read.FileLength,
                        state.Framer.PendingByteCount,
                        state.Checkpoint.UpdatedAtUtc);
                    if (state.HasPendingBytes)
                    {
                        telemetry.RecordPartialRecord(
                            state.Descriptor.SourceId,
                            state.Descriptor.FileId,
                            state.Framer.PendingByteCount);
                    }

                    return state.HasPendingBytes
                        ? FileTurnResult.WaitingForMoreData()
                        : FileTurnResult.CaughtUp();
                }

                state.SetReadOffset(read.NextOffset);
                bytesReadThisTurn += read.Data.Length;

                try
                {
                    var framedRecords = state.Framer.Append(read.Data, read.StartOffset).ToArray();
                    state.EnqueueRecords(framedRecords);
                    telemetry.RecordRecordsFramed(
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId,
                        framedRecords.Length);
                    telemetry.RecordProgress(
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId,
                        state.Checkpoint.Position,
                        read.FileLength,
                        state.Framer.PendingByteCount,
                        state.Checkpoint.UpdatedAtUtc);
                    if (state.HasPendingBytes)
                    {
                        telemetry.RecordPartialRecord(
                            state.Descriptor.SourceId,
                            state.Descriptor.FileId,
                            state.Framer.PendingByteCount);
                    }
                }
                catch (RawLogRecordTooLargeException exception)
                {
                    state.ResetToCheckpoint();
                    telemetry.RecordOversizedRecord(
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId);
                    return FileTurnResult.Stopped(exception);
                }
            }

            if (state.TryDequeueRecord(out var record) && record is not null)
            {
                var context = RawRecordContextFactory.Create(state.Descriptor, record);
                RawRecordPersistenceOutcome persistenceOutcome;
                try
                {
                    var processingResult = recordHandler.Handle(context);
                    using var recordScope = LoggingScopes.BeginFileScope(
                        logger,
                        workerOptions.Value.WorkerId,
                        state.Descriptor,
                        record.StartOffset,
                        record.EndOffsetExclusive,
                        processingResult.Event?.EventId,
                        processingResult.Failure?.FailureId,
                        result: processingResult.ParseStatus?.ToString());
                    if (processingResult.ParseStatus.HasValue)
                    {
                        telemetry.RecordParseResult(
                            state.Descriptor.SourceId,
                            state.Descriptor.FileId,
                            processingResult.ParseStatus.Value);
                    }

                    persistenceOutcome = await persistenceCoordinator.PersistAsync(
                        processingResult,
                        state.Checkpoint,
                        state.LastObservedFileLength,
                        workerOptions.Value.WorkerId,
                        cancellationToken);
                    logger.LogDebug(
                        AppConst.Logging.FileRecordProcessedMessage,
                        persistenceOutcome.WasFailure
                            ? AppConst.Observability.ResultFailure
                            : AppConst.Observability.ResultHistory,
                        record.StartOffset,
                        record.EndOffsetExclusive);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    state.ResetToCheckpoint();
                    throw;
                }
                catch (Exception exception)
                {
                    state.ResetToCheckpoint();
                    telemetry.RecordCheckpointAdvance(
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId,
                        record.EndOffsetExclusive,
                        false);
                    return FileTurnResult.PersistenceFailed(exception);
                }

                if (!persistenceOutcome.IsConfirmed)
                {
                    telemetry.RecordCheckpointAdvance(
                        state.Descriptor.SourceId,
                        state.Descriptor.FileId,
                        record.EndOffsetExclusive,
                        false);
                    if (persistenceOutcome.CheckpointResult.Status == CheckpointAdvanceStatus.Conflict)
                    {
                        await ReloadCheckpointAsync(state, cancellationToken);
                        return FileTurnResult.CheckpointConflict();
                    }

                    state.ResetToCheckpoint();
                    return FileTurnResult.PersistenceFailed(
                        new InvalidOperationException(AppConst.Messages.MSG_CHECKPOINT_CONFIRMATION_REQUIRED));
                }

                state.CommitCheckpoint(
                    persistenceOutcome.CheckpointResult.Checkpoint ??
                    throw new InvalidOperationException(AppConst.Messages.MSG_CHECKPOINT_CONFIRMATION_REQUIRED));
                telemetry.RecordCheckpointAdvance(
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    record.EndOffsetExclusive,
                    true);
                telemetry.RecordProgress(
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    state.Checkpoint.Position,
                    state.LastObservedFileLength,
                    state.Framer.PendingByteCount,
                    state.Checkpoint.UpdatedAtUtc);
                recordsProcessedThisTurn++;
                continue;
            }

            if (lastReadHadMoreData)
            {
                continue;
            }

            return state.HasPendingBytes
                ? FileTurnResult.WaitingForMoreData()
                : FileTurnResult.CaughtUp();
        }

        return FileTurnResult.Requeue();
    }

    private static bool HasBudget(
        long startedAt,
        int bytesRead,
        int recordsProcessed,
        RfidRawLogOptions options,
        TimeProvider timeProvider) =>
        (bytesRead == 0 && recordsProcessed == 0) ||
        (bytesRead < options.MaxBytesPerTurn &&
         recordsProcessed < options.MaxRecordsPerTurn &&
         timeProvider.GetElapsedTime(startedAt) < options.MaxTurnDuration);

    private async Task ReloadCheckpointAsync(
        FileIngestionState state,
        CancellationToken cancellationToken)
    {
        var key = new IngestionCheckpointKey
        {
            SourceId = state.Descriptor.SourceId,
            FolderDate = state.Descriptor.FolderDate,
            FileId = state.Descriptor.FileId,
            RelativePath = state.Descriptor.RelativePath
        };
        var latest = await checkpointStore.LoadAsync(key, cancellationToken);
        if (latest is not null && latest.Position >= state.Checkpoint.Position)
        {
            state.CommitCheckpoint(latest);
        }

        state.ResetToCheckpoint();
    }
}
