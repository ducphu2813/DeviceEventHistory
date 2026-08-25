using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.Persistence;

public interface IDeviceEventHistoryWriter
{
    Task<PersistenceWriteResult> WriteAsync(
        CanonicalDeviceEvent deviceEvent,
        DateTimeOffset receivedAtUtc,
        string workerId,
        CancellationToken cancellationToken);
}
