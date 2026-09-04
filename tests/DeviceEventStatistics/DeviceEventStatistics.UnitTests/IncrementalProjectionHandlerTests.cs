using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.UnitTests;

public sealed class IncrementalProjectionHandlerTests
{
    [Fact]
    public async Task Builds_atomic_batch_with_metric_quality_summary_and_checkpoint()
    {
        var persistedAt = new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);
        var historyEvent = CreateEvent(persistedAt) with
        {
            SourceEventName = "raw_record",
            ParseStatus = "parsed_with_warnings",
            Facts = new HistoryFacts { TagRead = new TagReadFacts("tag-1", null, null) }
        };
        var reader = new StubHistoryReader(new HistoryReadResult(
            [historyEvent],
            new SourceCursor(persistedAt, historyEvent.EventId!),
            persistedAt.AddMinutes(5),
            true));
        var handler = CreateHandler(
            reader,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["tag_read"] = 7 });
        var identity = ProjectionIdentity.Default();
        var lease = new ProjectionLeaseToken(identity, "worker-1", 1, persistedAt.AddHours(1));

        var result = await handler.PreparePageAsync(
            CreateOptions(identity, persistedAt.AddDays(-1)),
            lease);

        Assert.Equal(1, result.ReadEventCount);
        Assert.True(result.IsCaughtUp);
        Assert.Single(result.Batch.ProcessedEvents);
        Assert.Equal(7, Assert.Single(result.Batch.MetricContributions).MetricKey);
        Assert.Single(result.Batch.DeviceSummaries);
        Assert.Contains(result.Batch.QualityContributions, value => value.QualityCode == "parsed_with_warnings");
        Assert.Equal(historyEvent.EventId, result.Batch.NextCheckpoint.LastEventId);
        Assert.Null(result.Batch.NextCheckpoint.SweepFromAtUtc);
    }

    [Fact]
    public async Task Empty_page_completes_in_progress_sweep_without_moving_high_watermark()
    {
        var identity = ProjectionIdentity.Default();
        var checkpoint = new ProjectionCheckpoint(
            identity,
            DateTimeOffset.UnixEpoch,
            new string('a', 64),
            SweepFromAtUtc: DateTimeOffset.UnixEpoch,
            SweepToAtUtc: DateTimeOffset.UnixEpoch.AddHours(1),
            SweepLastPersistedAtUtc: DateTimeOffset.UnixEpoch.AddMinutes(30),
            SweepLastEventId: new string('b', 64));
        var reader = new StubHistoryReader(new HistoryReadResult(
            [],
            new SourceCursor(DateTimeOffset.UnixEpoch.AddMinutes(30), new string('b', 64)),
            DateTimeOffset.UnixEpoch.AddHours(1),
            true));
        var handler = CreateHandler(reader, new Dictionary<string, int>(), checkpoint);
        var lease = new ProjectionLeaseToken(identity, "worker-1", 1, DateTimeOffset.UtcNow.AddHours(1));

        var result = await handler.PreparePageAsync(
            CreateOptions(identity, DateTimeOffset.UnixEpoch),
            lease);

        Assert.Empty(result.Batch.ProcessedEvents);
        Assert.Equal(checkpoint.SweepLastEventId, result.Batch.NextCheckpoint.LastEventId);
        Assert.Null(result.Batch.NextCheckpoint.SweepFromAtUtc);
    }

    [Fact]
    public async Task Invalid_event_identity_becomes_terminal_failure_without_processed_event()
    {
        var persistedAt = new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);
        var reader = new StubHistoryReader(new HistoryReadResult(
            [CreateEvent(persistedAt) with { EventId = "not-a-sha256" }],
            null,
            persistedAt.AddMinutes(5),
            true));
        var identity = ProjectionIdentity.Default();
        var handler = CreateHandler(reader, new Dictionary<string, int>());
        var lease = new ProjectionLeaseToken(identity, "worker-1", 1, persistedAt.AddHours(1));

        var result = await handler.PreparePageAsync(
            CreateOptions(identity, persistedAt.AddDays(-1)),
            lease);

        Assert.Empty(result.Batch.ProcessedEvents);
        Assert.Single(result.Batch.Failures);
        Assert.Equal("STAT_EVENT_ID_INVALID", result.Batch.Failures[0].ErrorCode);
    }

    [Fact]
    public async Task Maps_confirmed_device_connection_events_to_state_observations()
    {
        var persistedAt = new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);
        var historyEvent = CreateEvent(persistedAt) with
        {
            EventId = new string('b', 64),
            SourceKind = "erp_apphub",
            Category = "device_connection",
            SourceEventName = "receiveStateConnected",
            Facts = new HistoryFacts { Connection = new ConnectionFacts("connected", false, true, true) }
        };
        var handler = new IncrementalProjectionHandler(
            new StubHistoryReader(new HistoryReadResult(
                [historyEvent],
                new SourceCursor(persistedAt, historyEvent.EventId),
                persistedAt.AddMinutes(5),
                true)),
            new StubCheckpointStore(),
            new ProjectionEventOutcomeMapper(
                new HistoryEventEligibilityPolicy(),
                new EventOwnershipPolicy(),
                new DeviceMetricMapperRegistry([new AppHubConnectionMetricMapper()]),
                new LocalStatisticsDateResolver()),
            new StubMetricKeyResolver(new Dictionary<string, int> { ["device_connected"] = 7 }),
            new LocalStatisticsDateResolver(),
            new FixedTimeProvider(persistedAt.AddHours(1)));
        var identity = ProjectionIdentity.Default();
        var lease = new ProjectionLeaseToken(identity, "worker-1", 1, persistedAt.AddHours(1));

        var result = await handler.PreparePageAsync(CreateOptions(identity, persistedAt.AddDays(-1)), lease);

        var observation = Assert.Single(result.Batch.StateObservations);
        Assert.Equal("device_connection", observation.StateType);
        Assert.Equal("connected", observation.ObservedState);
    }

    private static IncrementalProjectionHandler CreateHandler(
        IHistoryEventReader reader,
        IReadOnlyDictionary<string, int> metricKeys,
        ProjectionCheckpoint? checkpoint = null) =>
        new(
            reader,
            new StubCheckpointStore(checkpoint),
            new ProjectionEventOutcomeMapper(
                new HistoryEventEligibilityPolicy(),
                new EventOwnershipPolicy(),
                new DeviceMetricMapperRegistry([new RawFileMetricMapper()]),
                new LocalStatisticsDateResolver()),
            new StubMetricKeyResolver(metricKeys),
            new LocalStatisticsDateResolver(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero)));

    private static IncrementalProjectionOptions CreateOptions(
        ProjectionIdentity identity,
        DateTimeOffset coverageStart) =>
        new(identity, "v1", 1, coverageStart, 10, 20, TimeSpan.FromMinutes(5), TimeSpan.Zero, [], []);

    private static HistoryEvent CreateEvent(DateTimeOffset persistedAt) => new()
    {
        SourceDocumentId = "source-document-1",
        EventId = new string('a', 64),
        SchemaVersion = 1,
        CompanyId = 2,
        Category = "tag_read",
        SourceKind = "rfid_antenna_file",
        SourceId = "source-1",
        OccurredAtUtc = persistedAt,
        PersistedAtUtc = persistedAt,
        TimelineAtUtc = persistedAt,
        TimeBasis = "occurred",
        DeviceId = 101,
        ParseStatus = "parsed"
    };

    private sealed class StubHistoryReader(HistoryReadResult result) : IHistoryEventReader
    {
        public Task<HistoryReadResult> ReadPageAsync(
            DateTimeOffset fromPersistedAtUtc,
            DateTimeOffset toPersistedAtUtc,
            SourceCursor? after,
            int pageSize,
            IReadOnlyCollection<long>? companyIds = null,
            IReadOnlyCollection<long>? deviceIds = null,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubCheckpointStore(ProjectionCheckpoint? checkpoint = null) : IProjectionCheckpointStore
    {
        public Task<ProjectionCheckpoint> GetOrCreateAsync(
            ProjectionIdentity identity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(checkpoint ?? new ProjectionCheckpoint(identity));

        public Task<bool> AdvanceAsync(
            ProjectionCheckpoint expected,
            ProjectionCheckpoint next,
            ProjectionLeaseToken lease,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class StubMetricKeyResolver(IReadOnlyDictionary<string, int> values) : IMetricKeyResolver
    {
        public Task<IReadOnlyDictionary<string, int>> ResolveAsync(
            int metricSetVersion,
            IReadOnlyCollection<string> metricCodes,
            CancellationToken cancellationToken = default) => Task.FromResult(values);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
