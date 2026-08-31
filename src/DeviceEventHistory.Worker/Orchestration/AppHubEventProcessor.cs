using System.Threading.Channels;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Application.Observability;
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
    ILogger<AppHubEventProcessor> logger,
    IIngestionTelemetry? telemetry = null)
{
    private readonly IIngestionTelemetry telemetry =
        telemetry ?? NullIngestionTelemetry.Instance;

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
            telemetry.RecordAppHubChannelDepth(sourceEvent.SourceId, reader.Count);
            try
            {
                CanonicalIngestionResult ingestionResult;
                try
                {
                    ingestionResult = sourceEvent.PayloadSizeBytes > maximumPayloadBytes
                        ? RawSourceEventFailureFactory.CreatePayloadTooLargeFailure(
                            sourceEvent,
                            maximumPayloadBytes)
                        : mapperRegistry.Map(sourceEvent);
                }
                catch
                {
                    telemetry.RecordAppHubMappingResult(
                        sourceEvent.SourceId,
                        AppConst.Parsing.StatusFailure);
                    throw;
                }

                telemetry.RecordAppHubMappingResult(
                    sourceEvent.SourceId,
                    ingestionResult.Event?.Parse.Status ?? AppConst.Parsing.StatusFailure);

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

        telemetry.RecordAppHubChannelDepth(sourceId, reader.Count);

        return processedCount;
    }
}
