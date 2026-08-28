using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Application.Ingestion;

namespace DeviceEventHistory.Application.Persistence;

public sealed class RawRecordPersistenceCoordinator(
    ICanonicalIngestionPersistenceService persistenceService,
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

        var ingestionResult = new CanonicalIngestionResult
        {
            Event = processingResult.Event,
            Failure = processingResult.Failure
        };
        ingestionResult.EnsureExactlyOneOutcome();

        var source = ingestionResult.Event?.Source ?? ingestionResult.Failure!.Source;
        var rawFileContext = RequireRawFileContext(source);
        var payloadHash = ingestionResult.Event?.RawPayload.Sha256
            ?? ingestionResult.Failure!.RawPayload.Sha256;
        var persistenceResult = await persistenceService.PersistAsync(
            ingestionResult,
            workerId,
            cancellationToken);

        return await AdvanceCheckpointAsync(
            checkpoint,
            source.SourceId,
            rawFileContext.RelativePath,
            rawFileContext.FolderDate,
            rawFileContext.FileId,
            rawFileContext.OffsetEnd,
            payloadHash,
            persistenceResult.PersistedIdentity,
            observedFileLength,
            workerId,
            cancellationToken) with
        {
            PersistedIdentity = persistenceResult.PersistedIdentity,
            WasFailure = persistenceResult.WasFailure,
            WasAlreadyPersisted = persistenceResult.WasAlreadyPersisted
        };
    }

    private static RawFileContext RequireRawFileContext(
        CanonicalDeviceEvent.SourceContext source)
    {
        if (source.FileId is not long fileId ||
            string.IsNullOrWhiteSpace(source.RelativePath) ||
            source.FolderDate is not DateOnly folderDate ||
            source.OffsetEnd is not long offsetEnd)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_RAW_RECORD_FILE_SOURCE_CONTEXT_REQUIRED);
        }

        return new RawFileContext(source.RelativePath, folderDate, fileId, offsetEnd);
    }

    private sealed record RawFileContext(
        string RelativePath,
        DateOnly FolderDate,
        long FileId,
        long OffsetEnd);

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
