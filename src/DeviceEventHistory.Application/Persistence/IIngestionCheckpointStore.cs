namespace DeviceEventHistory.Application.Persistence;

public interface IIngestionCheckpointStore
{
    Task<IngestionCheckpoint?> LoadAsync(
        IngestionCheckpointKey key,
        CancellationToken cancellationToken);

    Task<CheckpointAdvanceResult> AdvanceAsync(
        IngestionCheckpointKey key,
        long expectedVersion,
        CheckpointAdvanceRequest request,
        CancellationToken cancellationToken);
}
