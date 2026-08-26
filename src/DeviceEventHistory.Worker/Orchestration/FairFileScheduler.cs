using System.Threading.Channels;
using System.Collections.Concurrent;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class FairFileScheduler(
    int maxConcurrentFiles,
    int queueCapacity,
    FileTurnProcessor processor,
    ILogger<FairFileScheduler> logger,
    IIngestionTelemetry? ingestionTelemetry = null,
    IOptions<WorkerOptions>? workerOptions = null)
{
    private readonly IIngestionTelemetry telemetry =
        ingestionTelemetry ?? NullIngestionTelemetry.Instance;
    private readonly string workerId = workerOptions?.Value.WorkerId ?? AppConst.Defaults.WorkerId;
    private readonly ConcurrentDictionary<string, byte> observedFileStates = new(StringComparer.Ordinal);
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
        logger.LogInformation(
            AppConst.Logging.SchedulerStartedMessage,
            maxConcurrentFiles);

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
            if (observedFileStates.TryAdd(state.Key, 0))
            {
                logger.LogDebug(
                    AppConst.Logging.FileTurnStartedMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    state.Checkpoint.Position,
                    state.ReadOffset);
            }
            telemetry.RecordFileProcessingStarted(state.Descriptor.SourceId, state.Descriptor.FileId);
            using var fileScope = LoggingScopes.BeginFileScope(
                logger,
                workerId,
                state.Descriptor,
                state.Checkpoint.Position,
                state.ReadOffset);
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
                telemetry.RecordFileProcessingCompleted(
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    AppConst.Observability.ResultCanceled);
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
            var turnWasIdle = result.Status == FileTurnStatus.CaughtUp &&
                state.ReadyRecordCount == 0 &&
                state.Framer.PendingByteCount == 0;
            if (turnWasIdle)
            {
                logger.LogTrace(
                    AppConst.Logging.FileTurnCompletedMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    result.Status,
                    state.Checkpoint.Position,
                    state.ReadOffset,
                    state.ReadyRecordCount,
                    state.Framer.PendingByteCount);
            }
            else
            {
                logger.LogDebug(
                    AppConst.Logging.FileTurnCompletedMessage,
                    state.Descriptor.SourceId,
                    state.Descriptor.FileId,
                    result.Status,
                    state.Checkpoint.Position,
                    state.ReadOffset,
                    state.ReadyRecordCount,
                    state.Framer.PendingByteCount);
            }
            telemetry.RecordFileProcessingCompleted(
                state.Descriptor.SourceId,
                state.Descriptor.FileId,
                result.Status.ToString());
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
                TryScheduleAfterTurn(state);
            }
        }
    }

    private void TryScheduleAfterTurn(FileIngestionState state)
    {
        if (state.IsStopped || !state.TryRequestSchedule())
        {
            return;
        }

        if (queue.Writer.TryWrite(state))
        {
            return;
        }

        // A consumer must not wait for queue capacity while it is responsible
        // for consuming that same queue. The polling loop will schedule this
        // ready state again on its next pass when capacity becomes available.
        state.ClearScheduleRequest();
        logger.LogTrace(
            AppConst.Logging.FileRequeueDeferredMessage,
            state.Descriptor.SourceId,
            state.Descriptor.FileId);
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
