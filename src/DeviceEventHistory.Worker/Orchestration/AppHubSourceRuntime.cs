using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Admission;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Logging;

namespace DeviceEventHistory.Worker.Orchestration;

/// <summary>
/// Owns one AppHub source connection, generation/sequence boundary, bounded queue
/// and FIFO consumer. A source runtime never shares a channel or connection with another source.
/// </summary>
public sealed class AppHubSourceRuntime : IAsyncDisposable
{
    private readonly AppHubSourceOptions source;
    private readonly IAppHubMonitoringConnectionFactory connectionFactory;
    private readonly AppHubCallbackRegistrar callbackRegistrar;
    private readonly AppHubEventAdmission admission;
    private readonly AppHubEventProcessor processor;
    private readonly IngestionHealthState healthState;
    private readonly TimeSpan shutdownTimeout;
    private readonly ILogger<AppHubSourceRuntime> logger;
    private int started;
    private int disposed;
    private IAppHubMonitoringConnection? activeConnection;

    public AppHubSourceRuntime(
        AppHubSourceOptions source,
        IAppHubMonitoringConnectionFactory connectionFactory,
        AppHubCallbackRegistrar callbackRegistrar,
        RawSourceEventMapperRegistry mapperRegistry,
        ICanonicalIngestionPersistenceService persistenceService,
        string workerId,
        int maximumPayloadBytes,
        TimeSpan shutdownTimeout,
        TimeProvider timeProvider,
        IngestionHealthState healthState,
        ILoggerFactory loggerFactory,
        IIngestionTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(callbackRegistrar);
        ArgumentNullException.ThrowIfNull(mapperRegistry);
        ArgumentNullException.ThrowIfNull(persistenceService);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(healthState);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        if (maximumPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        }

        if (shutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        }

        if (string.IsNullOrWhiteSpace(source.SourceId))
        {
            throw new ArgumentException(
                AppConst.Messages.MSG_APPHUB_RUNTIME_SOURCE_REQUIRED,
                nameof(source));
        }

        this.source = source;
        this.connectionFactory = connectionFactory;
        this.callbackRegistrar = callbackRegistrar;
        var ingestionTelemetry = telemetry ?? NullIngestionTelemetry.Instance;
        this.healthState = healthState;
        this.shutdownTimeout = shutdownTimeout;
        logger = loggerFactory.CreateLogger<AppHubSourceRuntime>();
        admission = new AppHubEventAdmission(source, timeProvider, ingestionTelemetry);
        processor = new AppHubEventProcessor(
            mapperRegistry,
            persistenceService,
            workerId,
            maximumPayloadBytes,
            loggerFactory.CreateLogger<AppHubEventProcessor>());
    }

    public string SourceId => source.SourceId.Trim();

    public int PendingCount => admission.Count;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException(
                AppConst.Messages.MSG_APPHUB_RUNTIME_ALREADY_STARTED);
        }

        using var processorCancellation = new CancellationTokenSource();
        var processorTask = processor.ProcessAsync(
            SourceId,
            admission.Reader,
            processorCancellation.Token);

        try
        {
            await RunConnectionLoopAsync(stoppingToken);
        }
        finally
        {
            admission.Complete();
            await DrainAsync(processorTask, processorCancellation);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        admission.Dispose();
        if (activeConnection is not null)
        {
            await StopAndDisposeConnectionAsync(activeConnection);
            activeConnection = null;
        }
    }

    private async Task RunConnectionLoopAsync(CancellationToken stoppingToken)
    {
        var reconnectAttempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            IAppHubMonitoringConnection? connection = null;
            IReadOnlyList<IDisposable> subscriptions = [];
            var disconnected = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                connection = connectionFactory.Create(source);
                activeConnection = connection;
                var connectionGeneration = connection.ConnectionGeneration;
                connection.StateChanged += state =>
                {
                    if (state == AppHubConnectionState.Running)
                    {
                        healthState.MarkSourceAvailable(SourceId);
                    }
                    else if (state == AppHubConnectionState.Disconnected)
                    {
                        disconnected.TrySetResult(true);
                    }
                };
                connection.LifecycleFailed += exception =>
                {
                    healthState.MarkSourceUnavailable(SourceId);
                    logger.LogWarning(
                        exception,
                        AppConst.Logging.AppHubSourceConnectionFailedMessage,
                        SourceId);
                    disconnected.TrySetResult(true);
                };

                subscriptions = callbackRegistrar.Register(
                    connection,
                    source.EnabledEvents ?? [],
                    (eventName, arguments) => HandleCallback(
                        connectionGeneration,
                        eventName,
                        arguments));

                await connection.StartAsync(stoppingToken);
                reconnectAttempt = 0;
                healthState.MarkSourceAvailable(SourceId);
                logger.LogInformation(
                    AppConst.Logging.AppHubSourceConnectedMessage,
                    SourceId,
                    connection.ConnectionGeneration);

                await disconnected.Task.WaitAsync(stoppingToken);
                logger.LogInformation(
                    AppConst.Logging.AppHubSourceDisconnectedMessage,
                    SourceId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                healthState.MarkSourceUnavailable(SourceId);
                logger.LogWarning(
                    exception,
                    AppConst.Logging.AppHubSourceConnectionFailedMessage,
                    SourceId);
            }
            finally
            {
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }

                if (connection is not null)
                {
                    await StopAndDisposeConnectionAsync(connection);
                    if (ReferenceEquals(activeConnection, connection))
                    {
                        activeConnection = null;
                    }
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await DelayBeforeReconnectAsync(reconnectAttempt++, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void HandleCallback(
        string connectionGeneration,
        string eventName,
        object[] arguments)
    {
        var admissionResult = admission.Admit(
            eventName,
            arguments,
            connectionGeneration);
        if (!admissionResult.IsAdmitted)
        {
            logger.LogWarning(
                AppConst.Logging.AppHubCallbackDroppedMessage,
                SourceId,
                eventName,
                admissionResult.DropReason);
        }
    }

    private async Task DelayBeforeReconnectAsync(
        int reconnectAttempt,
        CancellationToken stoppingToken)
    {
        var exponentialSeconds = source.ReconnectMinDelay.TotalSeconds *
            Math.Pow(2, Math.Min(reconnectAttempt, 30));
        var cappedSeconds = Math.Min(
            source.ReconnectMaxDelay.TotalSeconds,
            exponentialSeconds);
        var jitteredSeconds = cappedSeconds * (0.5 + (Random.Shared.NextDouble() * 0.5));
        await Task.Delay(TimeSpan.FromSeconds(jitteredSeconds), stoppingToken);
    }

    private async Task DrainAsync(
        Task<int> processorTask,
        CancellationTokenSource processorCancellation)
    {
        try
        {
            var processedCount = await processorTask.WaitAsync(shutdownTimeout);
            logger.LogInformation(
                AppConst.Logging.AppHubChannelDrainedMessage,
                SourceId,
                processedCount);
        }
        catch (TimeoutException)
        {
            var remainingCount = admission.Count;
            logger.LogWarning(
                AppConst.Logging.AppHubChannelDrainTimeoutMessage,
                SourceId,
                remainingCount);
            processorCancellation.Cancel();
            try
            {
                await processorTask;
            }
            catch (OperationCanceledException) when (processorCancellation.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task StopAndDisposeConnectionAsync(
        IAppHubMonitoringConnection connection)
    {
        try
        {
            await connection.StopAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Disposal still runs when a transport has already closed.
        }

        await connection.DisposeAsync();
    }
}
