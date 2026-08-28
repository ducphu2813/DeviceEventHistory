using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.Application.Persistence;

public interface IIngestionFailureWriter
{
    Task<PersistenceWriteResult> WriteAsync(
        CanonicalIngestionFailure failure,
        DateTimeOffset receivedAtUtc,
        string workerId,
        CancellationToken cancellationToken);
}
