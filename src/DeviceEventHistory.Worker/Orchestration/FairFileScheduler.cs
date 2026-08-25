using System.Threading.Channels;
using DeviceEventHistory.Domain.Common;
using Microsoft.Extensions.Logging;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class FairFileScheduler(
    int maxConcurrentFiles,
    int queueCapacity,
    FileTurnProcessor processor,
    ILogger<FairFileScheduler> logger)
{
    private readonly Channel<FileIngestionState> queue = Channel.CreateBounded<FileIngestionState>(
        new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public async Task ScheduleAsync(
        FileIngestionState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsStopped || !state.TryRequestSchedule())
        {
            return;
        }

        try
        {
            await queue.Writer.WriteAsync(state, cancellationToken);
        }
        catch
        {
            state.ClearScheduleRequest();
            throw;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var consumers = Enumerable
            .Range(0, maxConcurrentFiles)
            .Select(_ => ConsumeAsync(cancellationToken))
            .ToArray();

        await Task.WhenAll(consumers);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var state in queue.Reader.ReadAllAsync(cancellationToken))
        {
            if (state.IsStopped)
            {
                state.ClearScheduleRequest();
                continue;
            }

            state.SetStatus(FileIngestionStateStatus.Processing);
            FileTurnResult result;
            try
            {
                result = await processor.ProcessAsync(state, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                state.ResetToCheckpoint();
                state.SetStatus(FileIngestionStateStatus.Ready);
                state.ClearScheduleRequest();
                throw;
            }
            catch (Exception exception)
            {
                state.ResetToCheckpoint();
                state.SetStatus(FileIngestionStateStatus.Faulted);
                logger.LogError(
                    exception,
                    AppConst.Logging.FileProcessingFailedMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    state.Descriptor.FolderDate,
                    state.Checkpoint.Position);
                result = FileTurnResult.Failed(exception);
            }

            ApplyResultStatus(state, result);
            state.ClearScheduleRequest();

            if (result.Status == FileTurnStatus.Truncated)
            {
                logger.LogError(
                    AppConst.Logging.FileTruncatedMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    state.Descriptor.FolderDate,
                    state.Checkpoint.Position);
            }
            else if (result.Status == FileTurnStatus.CheckpointConflict)
            {
                logger.LogError(
                    AppConst.Logging.FileCheckpointConflictMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    state.Descriptor.FolderDate);
            }
            else if (result.Status == FileTurnStatus.Stopped)
            {
                logger.LogError(
                    result.Error,
                    AppConst.Logging.FileTurnStoppedMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    state.Descriptor.FolderDate);
            }
            else if (result.Status is FileTurnStatus.PersistenceFailed or FileTurnStatus.Failed)
            {
                logger.LogError(
                    result.Error,
                    AppConst.Logging.FileProcessingFailedMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    state.Descriptor.FolderDate,
                    state.Checkpoint.Position);
            }

            if ((result.ShouldRequeue || state.ConsumeWakeRequest()) && !state.IsStopped)
            {
                await ScheduleAsync(state, cancellationToken);
            }
        }
    }

    private static void ApplyResultStatus(FileIngestionState state, FileTurnResult result)
    {
        state.SetStatus(result.Status switch
        {
            FileTurnStatus.CaughtUp => FileIngestionStateStatus.CaughtUp,
            FileTurnStatus.WaitingForMoreData => FileIngestionStateStatus.WaitingForMoreData,
            FileTurnStatus.Truncated or FileTurnStatus.Stopped => FileIngestionStateStatus.Stopped,
            FileTurnStatus.Failed or FileTurnStatus.PersistenceFailed or FileTurnStatus.CheckpointConflict => FileIngestionStateStatus.Faulted,
            _ => FileIngestionStateStatus.Ready
        });
    }
}
