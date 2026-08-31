using DeviceEventHistory.Application.Ingestion;

namespace DeviceEventHistory.Application.Persistence;

public interface ICanonicalIngestionPersistenceService
{
    Task<CanonicalIngestionPersistenceOutcome> PersistAsync(
        CanonicalIngestionResult ingestionResult,
        string workerId,
        CancellationToken cancellationToken);
}
