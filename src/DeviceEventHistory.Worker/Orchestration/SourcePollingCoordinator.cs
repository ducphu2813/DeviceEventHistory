using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class SourcePollingCoordinator(
    IRawLogFileDiscovery fileDiscovery,
    FileRegistry fileRegistry,
    FairFileScheduler scheduler,
    IOptions<RfidRawLogOptions> rawLogOptions,
    ILogger<SourcePollingCoordinator> logger,
    IIngestionTelemetry? ingestionTelemetry = null)
{
    private readonly IIngestionTelemetry telemetry =
        ingestionTelemetry ?? NullIngestionTelemetry.Instance;
    private readonly Dictionary<string, int> lastDiscoveredFileCounts = new(StringComparer.Ordinal);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startupExistingFile = true;
        var options = rawLogOptions.Value;

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var source in options.Sources.Where(source => source.Enabled))
            {
                IReadOnlyList<RawLogFileDescriptor> descriptors;
                try
                {
                    descriptors = await fileDiscovery.DiscoverAsync(source, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    telemetry.RecordSourceAccessFailure(source.SourceId, source.Mode.ToString());
                    logger.LogWarning(
                        exception,
                        AppConst.Logging.SourceDiscoveryFailedMessage,
                        source.SourceId);
                    continue;
                }

                telemetry.RecordFilesDiscovered(
                    source.SourceId,
                    source.Mode.ToString(),
                    descriptors.Count);

                var discoveryChanged = !lastDiscoveredFileCounts.TryGetValue(
                    source.SourceId,
                    out var previousFileCount) || previousFileCount != descriptors.Count;
                lastDiscoveredFileCounts[source.SourceId] = descriptors.Count;

                if (discoveryChanged)
                {
                    logger.LogDebug(
                        AppConst.Logging.SourceDiscoveryCompletedMessage,
                        source.SourceId,
                        source.Mode,
                        descriptors.Count);
                }
                else
                {
                    logger.LogTrace(
                        AppConst.Logging.SourceDiscoveryCompletedMessage,
                        source.SourceId,
                        source.Mode,
                        descriptors.Count);
                }

                foreach (var descriptor in descriptors)
                {
                    try
                    {
                        var state = await fileRegistry.GetOrCreateAsync(
                            descriptor,
                            startupExistingFile,
                            cancellationToken);
                        await scheduler.ScheduleAsync(state, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            AppConst.Logging.FileStateInitializationFailedMessage,
                            descriptor.SourceId,
                            descriptor.FileId,
                            descriptor.FolderDate);
                    }
                }
            }

            foreach (var state in fileRegistry.Snapshot())
            {
                if (!state.IsStopped)
                {
                    await scheduler.ScheduleAsync(state, cancellationToken);
                }
            }

            startupExistingFile = false;
            await Task.Delay(options.PollInterval, cancellationToken);
        }
    }
}
