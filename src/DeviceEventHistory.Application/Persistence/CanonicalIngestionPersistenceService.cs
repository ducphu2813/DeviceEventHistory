using System.Diagnostics;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Domain.Failures;

namespace DeviceEventHistory.Application.Persistence;

public sealed class CanonicalIngestionPersistenceService(
    IDeviceEventHistoryWriter historyWriter,
    IIngestionFailureWriter failureWriter,
    TimeProvider timeProvider) : ICanonicalIngestionPersistenceService
{
    public async Task<CanonicalIngestionPersistenceOutcome> PersistAsync(
        CanonicalIngestionResult ingestionResult,
        string workerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ingestionResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        ingestionResult.EnsureExactlyOneOutcome();
        var startedAt = Stopwatch.GetTimestamp();
        var receivedAtUtc = GetReceivedAtUtc(ingestionResult);
        var persistedAtUtc = timeProvider.GetUtcNow();
        var processingDurationMs = Math.Max(
            0,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        if (ingestionResult.Event is not null)
        {
            var deviceEvent = Enrich(
                ingestionResult.Event,
                receivedAtUtc,
                persistedAtUtc,
                processingDurationMs,
                workerId);
            var writeResult = await historyWriter.WriteAsync(
                deviceEvent,
                receivedAtUtc,
                workerId,
                cancellationToken);

            return new CanonicalIngestionPersistenceOutcome(
                writeResult.Identity,
                WasFailure: false,
                writeResult.WasAlreadyPersisted,
                receivedAtUtc,
                persistedAtUtc,
                processingDurationMs);
        }

        var failure = Enrich(
            ingestionResult.Failure!,
            receivedAtUtc,
            persistedAtUtc,
            processingDurationMs,
            workerId);
        var failureWriteResult = await failureWriter.WriteAsync(
            failure,
            receivedAtUtc,
            workerId,
            cancellationToken);

        return new CanonicalIngestionPersistenceOutcome(
            failureWriteResult.Identity,
            WasFailure: true,
            failureWriteResult.WasAlreadyPersisted,
            receivedAtUtc,
            persistedAtUtc,
            processingDurationMs);
    }

    private DateTimeOffset GetReceivedAtUtc(CanonicalIngestionResult ingestionResult) =>
        ingestionResult.Event?.ReceivedAtUtc
        ?? ingestionResult.Failure?.ReceivedAtUtc
        ?? timeProvider.GetUtcNow();

    private static CanonicalDeviceEvent Enrich(
        CanonicalDeviceEvent deviceEvent,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset persistedAtUtc,
        long processingDurationMs,
        string workerId) =>
        deviceEvent with
        {
            ReceivedAtUtc = receivedAtUtc,
            PersistedAtUtc = persistedAtUtc,
            TimelineAtUtc = deviceEvent.TimelineAtUtc
                ?? deviceEvent.OccurredAtUtc
                ?? receivedAtUtc,
            TimeBasis = deviceEvent.TimeBasis
                ?? (deviceEvent.OccurredAtUtc.HasValue
                    ? AppConst.TimeBases.Occurred
                    : AppConst.TimeBases.Received),
            Ingestion = (deviceEvent.Ingestion ?? new CanonicalDeviceEvent.IngestionContext
            {
                WorkerId = workerId
            }) with
            {
                WorkerId = workerId,
                ProcessingDurationMs = processingDurationMs
            }
        };

    private static CanonicalIngestionFailure Enrich(
        CanonicalIngestionFailure failure,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset persistedAtUtc,
        long processingDurationMs,
        string workerId) =>
        failure with
        {
            ReceivedAtUtc = receivedAtUtc,
            PersistedAtUtc = persistedAtUtc,
            Ingestion = (failure.Ingestion ?? new CanonicalDeviceEvent.IngestionContext
            {
                WorkerId = workerId
            }) with
            {
                WorkerId = workerId,
                ProcessingDurationMs = processingDurationMs
            }
        };

}
