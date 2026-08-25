using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class FileTurnProcessor(
    IRawLogTailReader tailReader,
    IProcessRawFileRecordHandler recordHandler,
    IRawRecordPersistenceCoordinator persistenceCoordinator,
    IIngestionCheckpointStore checkpointStore,
    IOptions<RfidRawLogOptions> rawLogOptions,
    IOptions<WorkerOptions> workerOptions,
    TimeProvider timeProvider)
{
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

        while (HasBudget(
            startedAt,
            bytesReadThisTurn,
            recordsProcessedThisTurn,
            options,
            timeProvider))
        {
            if (state.ReadyRecordCount == 0)
            {
                var read = await tailReader.ReadAsync(
                    state.Descriptor,
                    state.ReadOffset,
                    cancellationToken);
                state.SetLastObservedFileLength(read.FileLength);
                lastReadHadMoreData = read.HasMore;

                if (read.IsTruncated)
                {
                    state.ResetToCheckpoint();
                    return FileTurnResult.Truncated();
                }

                if (read.Data.Length == 0)
                {
                    return state.HasPendingBytes
                        ? FileTurnResult.WaitingForMoreData()
                        : FileTurnResult.CaughtUp();
                }

                state.SetReadOffset(read.NextOffset);
                bytesReadThisTurn += read.Data.Length;

                try
                {
                    state.EnqueueRecords(
                        state.Framer.Append(read.Data, read.StartOffset));
                }
                catch (RawLogRecordTooLargeException exception)
                {
                    state.ResetToCheckpoint();
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
                    persistenceOutcome = await persistenceCoordinator.PersistAsync(
                        processingResult,
                        state.Checkpoint,
                        state.LastObservedFileLength,
                        workerOptions.Value.WorkerId,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    state.ResetToCheckpoint();
                    throw;
                }
                catch (Exception exception)
                {
                    state.ResetToCheckpoint();
                    return FileTurnResult.PersistenceFailed(exception);
                }

                if (!persistenceOutcome.IsConfirmed)
                {
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
