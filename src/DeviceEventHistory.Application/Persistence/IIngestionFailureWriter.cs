using DeviceEventHistory.Application.Parsing;

namespace DeviceEventHistory.Application.Persistence;

public interface IIngestionFailureWriter
{
    Task<PersistenceWriteResult> WriteAsync(
        RawRecordProcessingResult.CanonicalIngestionFailure failure,
        DateTimeOffset receivedAtUtc,
        string workerId,
        CancellationToken cancellationToken);
}
