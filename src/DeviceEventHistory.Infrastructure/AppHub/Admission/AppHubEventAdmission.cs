using System.Threading.Channels;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;

namespace DeviceEventHistory.Infrastructure.AppHub.Admission;

/// <summary>
/// Owns one bounded FIFO admission queue for one AppHub source.
/// The SignalR callback only creates an envelope and performs bounded admission.
/// </summary>
public sealed class AppHubEventAdmission : IDisposable
{
    private readonly Channel<RawSourceEvent> channel;
    private readonly AppHubRawSourceEventFactory envelopeFactory;
    private readonly TimeSpan enqueueTimeout;
    private readonly IIngestionTelemetry telemetry;
    private readonly CancellationTokenSource admissionCancellation = new();
    private int completed;

    public AppHubEventAdmission(
        AppHubSourceOptions source,
        TimeProvider timeProvider,
        IIngestionTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (source.ChannelCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source.ChannelCapacity));
        }

        if (source.EnqueueTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(source.EnqueueTimeout));
        }

        channel = Channel.CreateBounded<RawSourceEvent>(
            new BoundedChannelOptions(source.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        envelopeFactory = new AppHubRawSourceEventFactory(source, timeProvider);
        enqueueTimeout = source.EnqueueTimeout;
        this.telemetry = telemetry ?? NullIngestionTelemetry.Instance;
        SourceId = source.SourceId.Trim();
    }

    public string SourceId { get; }

    public ChannelReader<RawSourceEvent> Reader => channel.Reader;

    public int Count => channel.Reader.Count;

    public AppHubAdmissionResult Admit(
        string eventName,
        object[]? arguments,
        string connectionGeneration)
    {
        if (Volatile.Read(ref completed) != 0)
        {
            return Drop(eventName, AppConst.Observability.AppHubAdmissionChannelClosed);
        }

        var normalizedEventName = eventName?.Trim() ?? string.Empty;
        telemetry.RecordAppHubCallbackReceived(SourceId, normalizedEventName);

        RawSourceEvent sourceEvent;
        try
        {
            sourceEvent = envelopeFactory.Create(connectionGeneration, normalizedEventName, arguments);
        }
        catch (Exception)
        {
            return Drop(normalizedEventName, AppConst.Observability.AppHubAdmissionSerializationFailed);
        }

        if (channel.Writer.TryWrite(sourceEvent))
        {
            telemetry.RecordAppHubCallbackAdmitted(SourceId, normalizedEventName);
            telemetry.RecordAppHubChannelDepth(SourceId, channel.Reader.Count);
            return AppHubAdmissionResult.Admitted(sourceEvent);
        }

        try
        {
            var canWrite = channel.Writer
                .WaitToWriteAsync(admissionCancellation.Token)
                .AsTask()
                .WaitAsync(enqueueTimeout, admissionCancellation.Token)
                .GetAwaiter()
                .GetResult();

            if (canWrite && channel.Writer.TryWrite(sourceEvent))
            {
                telemetry.RecordAppHubCallbackAdmitted(SourceId, normalizedEventName);
                telemetry.RecordAppHubChannelDepth(SourceId, channel.Reader.Count);
                return AppHubAdmissionResult.Admitted(sourceEvent);
            }

            if (Volatile.Read(ref completed) == 0)
            {
                telemetry.RecordAppHubChannelSaturation(SourceId);
            }

            return Drop(
                normalizedEventName,
                Volatile.Read(ref completed) != 0
                    ? AppConst.Observability.AppHubAdmissionChannelClosed
                    : AppConst.Observability.AppHubAdmissionEnqueueTimeout);
        }
        catch (TimeoutException)
        {
            telemetry.RecordAppHubChannelSaturation(SourceId);
            return Drop(normalizedEventName, AppConst.Observability.AppHubAdmissionEnqueueTimeout);
        }
        catch (OperationCanceledException) when (admissionCancellation.IsCancellationRequested)
        {
            return Drop(normalizedEventName, AppConst.Observability.AppHubAdmissionChannelClosed);
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref completed, 1) == 0)
        {
            channel.Writer.TryComplete();
            admissionCancellation.Cancel();
        }
    }

    public void Dispose()
    {
        Complete();
        admissionCancellation.Dispose();
    }

    private AppHubAdmissionResult Drop(string eventName, string reason)
    {
        telemetry.RecordAppHubCallbackDropped(SourceId, eventName, reason);
        telemetry.RecordAppHubChannelDepth(SourceId, channel.Reader.Count);
        return AppHubAdmissionResult.Dropped(reason);
    }
}
