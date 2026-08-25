using DeviceEventHistory.Application.Parsing;

namespace DeviceEventHistory.Application.Persistence;

public interface IRawRecordPersistenceCoordinator
{
    Task<RawRecordPersistenceOutcome> PersistAsync(
        RawRecordProcessingResult processingResult,
        IngestionCheckpoint checkpoint,
        long? observedFileLength,
        string workerId,
        CancellationToken cancellationToken);
}
