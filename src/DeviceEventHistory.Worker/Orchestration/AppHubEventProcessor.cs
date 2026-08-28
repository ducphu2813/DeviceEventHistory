using System.Threading.Channels;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using Microsoft.Extensions.Logging;

namespace DeviceEventHistory.Worker.Orchestration;

/// <summary>
/// Consumes one source's admitted AppHub envelopes in FIFO order.
/// Mapping and persistence are deliberately outside the SignalR callback.
/// </summary>
public sealed class AppHubEventProcessor(
    RawSourceEventMapperRegistry mapperRegistry,
    ICanonicalIngestionPersistenceService persistenceService,
    string workerId,
    int maximumPayloadBytes,
    ILogger<AppHubEventProcessor> logger)
{
    public async Task<int> ProcessAsync(
        string sourceId,
        ChannelReader<RawSourceEvent> reader,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (maximumPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        }

        var processedCount = 0;
        await foreach (var sourceEvent in reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var ingestionResult = sourceEvent.PayloadSizeBytes > maximumPayloadBytes
                    ? RawSourceEventFailureFactory.CreatePayloadTooLargeFailure(
                        sourceEvent,
                        maximumPayloadBytes)
                    : mapperRegistry.Map(sourceEvent);

                ingestionResult.EnsureExactlyOneOutcome();
                await persistenceService.PersistAsync(
                    ingestionResult,
                    workerId,
                    cancellationToken);
                processedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    AppConst.Logging.AppHubCallbackProcessingFailedMessage,
                    sourceId,
                    sourceEvent.EventName);
            }
        }

        return processedCount;
    }
}
