using System.Security.Cryptography;
using System.Text;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.AppHub.Admission;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Worker.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeviceEventHistory.UnitTests;

public sealed class AppHubAdmissionAndRuntimeTests
{
    [Fact]
    public void Envelope_factory_serializes_ordered_arguments_and_assigns_identity()
    {
        var source = CreateSource();
        var factory = new AppHubRawSourceEventFactory(source, TimeProvider.System);

        var sourceEvent = factory.Create(
            "generation-1",
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            ["first", 2, null!]);

        Assert.Equal("[\"first\",2,null]", sourceEvent.RawArgumentsJson);
        Assert.Equal(
            Encoding.UTF8.GetByteCount(sourceEvent.RawArgumentsJson),
            sourceEvent.PayloadSizeBytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceEvent.RawArgumentsJson)))
                .ToLowerInvariant(),
            sourceEvent.PayloadSha256);
        Assert.Equal(
            RawSourceEventIdentityFactory.CreateEventId(sourceEvent),
            sourceEvent.IngestionEventId);
        Assert.Equal(1, sourceEvent.ReceiveSequence);
    }

    [Fact]
    public async Task Admission_preserves_fifo_and_reports_saturation_after_timeout()
    {
        var source = CreateSource();
        source.ChannelCapacity = 1;
        source.EnqueueTimeout = TimeSpan.FromMilliseconds(20);
        using var admission = new AppHubEventAdmission(source, TimeProvider.System);

        var first = admission.Admit("event", [1], "generation-1");
        var second = admission.Admit("event", [2], "generation-1");

        Assert.True(first.IsAdmitted);
        Assert.False(second.IsAdmitted);
        Assert.Equal(
            AppConst.Observability.AppHubAdmissionEnqueueTimeout,
            second.DropReason);
        Assert.Equal(1, admission.Count);
        var admittedEvent = await admission.Reader.ReadAsync();
        Assert.Equal(1, admittedEvent.ReceiveSequence);
    }

    [Fact]
    public void Oversized_payload_becomes_failure_with_hash_and_size_only()
    {
        var sourceEvent = new AppHubRawSourceEventFactory(CreateSource(), TimeProvider.System)
            .Create("generation-1", "event", ["payload"]);

        var result = RawSourceEventFailureFactory.CreatePayloadTooLargeFailure(
            sourceEvent,
            maximumPayloadBytes: 1);

        result.EnsureExactlyOneOutcome();
        Assert.NotNull(result.Failure);
        Assert.Equal(AppConst.Parsing.PayloadTooLarge, result.Failure!.Error.Code);
        Assert.Null(result.Failure.RawPayload.ArgumentsJson);
        Assert.Equal(sourceEvent.PayloadSha256, result.Failure.RawPayload.Sha256);
        Assert.Equal(sourceEvent.PayloadSizeBytes, result.Failure.RawPayload.SizeBytes);
    }

    [Fact]
    public async Task Source_runtime_registers_before_start_and_processes_admitted_events_in_fifo_order()
    {
        var source = CreateSource();
        var connection = new FakeMonitoringConnection(source.SourceId, "generation-1");
        var persistence = new RecordingPersistenceService();
        var runtime = new AppHubSourceRuntime(
            source,
            new FakeConnectionFactory(connection),
            new AppHubCallbackRegistrar(),
            new RawSourceEventMapperRegistry(
                [new TestMapper(AppConst.AppHub.Callbacks.ReceiveDeviceOnline)],
                new UnmappedRawSourceEventMapper()),
            persistence,
            "worker-01",
            maximumPayloadBytes: 1_024,
            shutdownTimeout: TimeSpan.FromSeconds(2),
            TimeProvider.System,
            CreateHealthState(),
            NullLoggerFactory.Instance);
        using var cancellation = new CancellationTokenSource();

        var runTask = runtime.RunAsync(cancellation.Token);
        await connection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        connection.Emit(["first"]);
        connection.Emit(["second"]);
        cancellation.Cancel();

        await runTask;
        await runtime.DisposeAsync();

        Assert.Equal(
            ["[\"first\"]", "[\"second\"]"],
            persistence.SourceEvents.Select(sourceEvent => sourceEvent.RawArgumentsJson).ToArray());
        Assert.Equal(
            ["register:receiveDeviceOnline", "start", "join"],
            connection.Calls);
    }

    [Fact]
    public async Task Source_runtime_rebuilds_generation_without_mixing_source_sequence_or_callbacks()
    {
        var source = CreateSource();
        var firstConnection = new FakeMonitoringConnection(source.SourceId, "generation-1");
        var secondConnection = new FakeMonitoringConnection(source.SourceId, "generation-2");
        var persistence = new RecordingPersistenceService();
        var runtime = new AppHubSourceRuntime(
            source,
            new QueueConnectionFactory(firstConnection, secondConnection),
            new AppHubCallbackRegistrar(),
            new RawSourceEventMapperRegistry(
                [new TestMapper(AppConst.AppHub.Callbacks.ReceiveDeviceOnline)],
                new UnmappedRawSourceEventMapper()),
            persistence,
            "worker-01",
            maximumPayloadBytes: 1_024,
            shutdownTimeout: TimeSpan.FromSeconds(2),
            TimeProvider.System,
            CreateHealthState(),
            NullLoggerFactory.Instance);
        using var cancellation = new CancellationTokenSource();

        var runTask = runtime.RunAsync(cancellation.Token);
        await firstConnection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        firstConnection.Emit(["first"]);
        firstConnection.RaiseClosed();
        await secondConnection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        secondConnection.Emit(["second"]);
        cancellation.Cancel();

        await runTask;
        await runtime.DisposeAsync();

        Assert.Equal(
            ["generation-1", "generation-2"],
            persistence.SourceEvents.Select(sourceEvent => sourceEvent.ConnectionGeneration).ToArray());
        Assert.Equal(
            [1L, 2L],
            persistence.SourceEvents.Select(sourceEvent => sourceEvent.ReceiveSequence).ToArray());
        Assert.Equal(1, firstConnection.RegistrationCount);
        Assert.Equal(1, secondConnection.RegistrationCount);
    }

    [Fact]
    public async Task Source_runtime_cancels_processor_when_shutdown_drain_times_out()
    {
        var source = CreateSource();
        var connection = new FakeMonitoringConnection(source.SourceId, "generation-1");
        var persistence = new BlockingPersistenceService();
        var runtime = new AppHubSourceRuntime(
            source,
            new FakeConnectionFactory(connection),
            new AppHubCallbackRegistrar(),
            new RawSourceEventMapperRegistry(
                [new TestMapper(AppConst.AppHub.Callbacks.ReceiveDeviceOnline)],
                new UnmappedRawSourceEventMapper()),
            persistence,
            "worker-01",
            maximumPayloadBytes: 1_024,
            shutdownTimeout: TimeSpan.FromMilliseconds(20),
            TimeProvider.System,
            CreateHealthState(),
            NullLoggerFactory.Instance);
        using var cancellation = new CancellationTokenSource();

        var runTask = runtime.RunAsync(cancellation.Token);
        await connection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        connection.Emit(["blocking"]);
        await persistence.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.DisposeAsync();
    }

    private static AppHubSourceOptions CreateSource() => new()
    {
        SourceId = "apphub-source",
        Endpoint = "https://erp.example.com/signalr",
        HubName = AppConst.AppHub.DefaultHubName,
        ChannelCapacity = 8,
        EnqueueTimeout = TimeSpan.FromMilliseconds(50),
        ReconnectMinDelay = TimeSpan.FromMilliseconds(1),
        ReconnectMaxDelay = TimeSpan.FromMilliseconds(10),
        EnabledEvents = [AppConst.AppHub.Callbacks.ReceiveDeviceOnline],
        AccessTokenEnvironmentVariable = "APPHUB_TEST_TOKEN"
    };

    private static IngestionHealthState CreateHealthState()
    {
        var state = new IngestionHealthState(
            TimeProvider.System,
            mongoFailureUnhealthyThreshold: 3,
            sourceFailureUnhealthyThreshold: 3,
            progressStaleAfter: TimeSpan.FromMinutes(5));
        state.ConfigureSources(["apphub-source"]);
        state.MarkLive();
        state.MarkStartupReady();
        return state;
    }

    private sealed class TestMapper(string eventName) : IRawSourceEventMapper
    {
        public string SourceKind => AppConst.AppHub.SourceKind;

        public string EventName => eventName;

        public CanonicalIngestionResult Map(RawSourceEvent sourceEvent) =>
            CanonicalIngestionResult.FromEvent(new CanonicalDeviceEvent
            {
                EventId = sourceEvent.IngestionEventId,
                SchemaVersion = AppConst.SchemaVersions.CanonicalV2,
                Category = AppConst.Categories.DeviceOnline,
                SourceKind = sourceEvent.SourceKind,
                CompanyId = 1,
                ReceivedAtUtc = sourceEvent.ReceivedAtUtc,
                Source = new CanonicalDeviceEvent.SourceContext
                {
                    Producer = sourceEvent.SourceApplication,
                    SourceId = sourceEvent.SourceId,
                    Transport = sourceEvent.SourceTransport,
                    EventName = sourceEvent.EventName,
                    DeliveryKind = sourceEvent.DeliveryKind,
                    ConnectionGeneration = sourceEvent.ConnectionGeneration,
                    ReceiveSequence = sourceEvent.ReceiveSequence
                },
                RawPayload = new CanonicalDeviceEvent.RawPayloadContext
                {
                    Format = AppConst.AppHub.PayloadFormat,
                    ArgumentsJson = sourceEvent.RawArgumentsJson,
                    Sha256 = sourceEvent.PayloadSha256,
                    SizeBytes = sourceEvent.PayloadSizeBytes
                },
                Facts = new CanonicalDeviceEvent.FactsContext(),
                Parse = new CanonicalDeviceEvent.ParseContext
                {
                    Status = AppConst.Parsing.StatusParsed,
                    ParserVersion = AppConst.AppHub.ParserVersion
                }
            });
    }

    private sealed class RecordingPersistenceService : ICanonicalIngestionPersistenceService
    {
        public List<RawSourceEvent> SourceEvents { get; } = [];

        public Task<CanonicalIngestionPersistenceOutcome> PersistAsync(
            CanonicalIngestionResult ingestionResult,
            string workerId,
            CancellationToken cancellationToken)
        {
            SourceEvents.Add(new RawSourceEvent
            {
                IngestionEventId = ingestionResult.Event!.EventId,
                SourceKind = ingestionResult.Event.SourceKind,
                SourceId = ingestionResult.Event.Source.SourceId,
                SourceApplication = ingestionResult.Event.Source.Producer,
                SourceTransport = ingestionResult.Event.Source.Transport!,
                EventName = ingestionResult.Event.Source.EventName!,
                ReceivedAtUtc = ingestionResult.Event.ReceivedAtUtc!.Value,
                RawArgumentsJson = ingestionResult.Event.RawPayload.ArgumentsJson!,
                PayloadSha256 = ingestionResult.Event.RawPayload.Sha256,
                PayloadSizeBytes = ingestionResult.Event.RawPayload.SizeBytes!.Value,
                ConnectionGeneration = ingestionResult.Event.Source.ConnectionGeneration!,
                ReceiveSequence = ingestionResult.Event.Source.ReceiveSequence!.Value,
                DeliveryKind = ingestionResult.Event.Source.DeliveryKind!
            });
            return Task.FromResult(new CanonicalIngestionPersistenceOutcome(
                ingestionResult.Identity,
                WasFailure: false,
                WasAlreadyPersisted: false,
                ingestionResult.Event.ReceivedAtUtc!.Value,
                ingestionResult.Event.ReceivedAtUtc.Value,
                ProcessingDurationMs: 0));
        }
    }

    private sealed class BlockingPersistenceService : ICanonicalIngestionPersistenceService
    {
        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CanonicalIngestionPersistenceOutcome> PersistAsync(
            CanonicalIngestionResult ingestionResult,
            string workerId,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The persistence delay should be cancelled.");
        }
    }

    private sealed class FakeConnectionFactory(FakeMonitoringConnection connection)
        : IAppHubMonitoringConnectionFactory
    {
        public IAppHubMonitoringConnection Create(AppHubSourceOptions source) => connection;
    }

    private sealed class QueueConnectionFactory(params FakeMonitoringConnection[] connections)
        : IAppHubMonitoringConnectionFactory
    {
        private readonly Queue<FakeMonitoringConnection> queue = new(connections);

        public IAppHubMonitoringConnection Create(AppHubSourceOptions source) => queue.Dequeue();
    }

    private sealed class FakeMonitoringConnection(string sourceId, string generation)
        : IAppHubMonitoringConnection
    {
        private readonly Dictionary<string, Action<object[]>> callbacks = new(StringComparer.Ordinal);
        private Action<AppHubConnectionState>? stateChanged;
        private Action<Exception>? lifecycleFailed;

        public List<string> Calls { get; } = [];

        public int RegistrationCount { get; private set; }

        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string SourceId => sourceId;

        public string ConnectionGeneration => generation;

        public AppHubConnectionState State { get; private set; } = AppHubConnectionState.Disconnected;

        public event Action<AppHubConnectionState>? StateChanged
        {
            add => stateChanged += value;
            remove => stateChanged -= value;
        }

        public event Action<Exception>? LifecycleFailed
        {
            add => lifecycleFailed += value;
            remove => lifecycleFailed -= value;
        }

        public IDisposable RegisterCallback(string eventName, Action<object[]> callback)
        {
            Calls.Add($"register:{eventName}");
            RegistrationCount++;
            callbacks[eventName] = callback;
            return new DelegateSubscription(() => callbacks.Remove(eventName));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Calls.Add("start");
            State = AppHubConnectionState.Running;
            stateChanged?.Invoke(State);
            Calls.Add("join");
            Started.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task JoinMonitoringAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            State = AppHubConnectionState.Disconnected;
            stateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public void RaiseClosed()
        {
            State = AppHubConnectionState.Disconnected;
            stateChanged?.Invoke(State);
        }

        public ValueTask DisposeAsync()
        {
            lifecycleFailed = null;
            return ValueTask.CompletedTask;
        }

        public void Emit(object[] arguments) => callbacks.Values.Single()(arguments);
    }

    private sealed class DelegateSubscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
