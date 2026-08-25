using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.Persistence;

public sealed class RawRecordPersistenceCoordinator(
    IDeviceEventHistoryWriter historyWriter,
    IIngestionFailureWriter failureWriter,
    IIngestionCheckpointStore checkpointStore,
    TimeProvider timeProvider) : IRawRecordPersistenceCoordinator
{
    public async Task<RawRecordPersistenceOutcome> PersistAsync(
        RawRecordProcessingResult processingResult,
        IngestionCheckpoint checkpoint,
        long? observedFileLength,
        string workerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processingResult);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        if (processingResult.Event is not null)
        {
            var eventContext = processingResult.Event.Source;
            var receivedAtUtc = timeProvider.GetUtcNow();
            var writeResult = await historyWriter.WriteAsync(
                processingResult.Event,
                receivedAtUtc,
                workerId,
                cancellationToken);

            return await AdvanceCheckpointAsync(
                checkpoint,
                eventContext.SourceId,
                eventContext.RelativePath,
                eventContext.FolderDate,
                eventContext.FileId,
                eventContext.OffsetEnd,
                processingResult.Event.RawPayload.Sha256,
                writeResult.Identity,
                observedFileLength,
                workerId,
                cancellationToken) with
            {
                PersistedIdentity = writeResult.Identity,
                WasFailure = false,
                WasAlreadyPersisted = writeResult.WasAlreadyPersisted
            };
        }

        if (processingResult.Failure is not null)
        {
            var failure = processingResult.Failure;
            var receivedAtUtc = timeProvider.GetUtcNow();
            var writeResult = await failureWriter.WriteAsync(
                failure,
                receivedAtUtc,
                workerId,
                cancellationToken);

            return await AdvanceCheckpointAsync(
                checkpoint,
                failure.Context.SourceId,
                failure.Context.RelativePath,
                failure.Context.FolderDate,
                failure.Context.FileId,
                failure.Context.OffsetEnd,
                EventIdentityFactory.ComputePayloadHash(failure.Context),
                writeResult.Identity,
                observedFileLength,
                workerId,
                cancellationToken) with
            {
                PersistedIdentity = writeResult.Identity,
                WasFailure = true,
                WasAlreadyPersisted = writeResult.WasAlreadyPersisted
            };
        }

        throw new InvalidOperationException(AppConst.Messages.MSG_PERSISTENCE_OUTCOME_REQUIRED);
    }

    private async Task<RawRecordPersistenceOutcome> AdvanceCheckpointAsync(
        IngestionCheckpoint checkpoint,
        string sourceId,
        string relativePath,
        DateOnly folderDate,
        long fileId,
        long offsetEnd,
        string recordHash,
        string persistedIdentity,
        long? observedFileLength,
        string workerId,
        CancellationToken cancellationToken)
    {
        var key = new IngestionCheckpointKey
        {
            SourceId = sourceId,
            RelativePath = relativePath,
            FolderDate = folderDate,
            FileId = fileId
        };

        if (offsetEnd < checkpoint.Position)
        {
            throw new InvalidOperationException(
                AppConst.Messages.Format(
                    AppConst.Messages.MSG_CHECKPOINT_POSITION_REGRESSION,
                    checkpoint.Position,
                    offsetEnd));
        }

        var result = await checkpointStore.AdvanceAsync(
            key,
            checkpoint.Version,
            new CheckpointAdvanceRequest
            {
                Position = offsetEnd,
                LastRecordHash = recordHash,
                LastEventId = persistedIdentity,
                ObservedFileLength = observedFileLength,
                WorkerId = workerId,
                UpdatedAtUtc = timeProvider.GetUtcNow()
            },
            cancellationToken);

        return new RawRecordPersistenceOutcome(
            persistedIdentity,
            false,
            false,
            result);
    }
}
