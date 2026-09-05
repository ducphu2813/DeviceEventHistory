using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Metadata;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.UnitTests;

public sealed class DeepDiscoveryTests
{
    [Fact]
    public async Task Audit_page_uses_independent_cursor_and_keeps_high_watermark_unchanged()
    {
        var now = new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
        var identity = ProjectionIdentity.Default();
        var checkpoint = new ProjectionCheckpoint(
            identity,
            LastPersistedAtUtc: now.AddDays(-1),
            LastEventId: new string('a', 64));
        var historyEvent = CreateEvent(now.AddDays(-2)) with
        {
            SourceDocumentId = "66d3b1a4f000000000000001"
        };
        var handler = CreateAuditHandler(
            new HistoryAuditResult([historyEvent], historyEvent.SourceDocumentId, false),
            checkpoint,
            now);
        var lease = new ProjectionLeaseToken(identity, "worker-1", 1, now.AddHours(1));

        var result = await handler.PreparePageAsync(
            CreateOptions(identity),
            lease);

        Assert.False(result.IsComplete);
        Assert.Equal(checkpoint.LastPersistedAtUtc, result.ProjectionPage.Batch.NextCheckpoint.LastPersistedAtUtc);
        Assert.Equal(checkpoint.LastEventId, result.ProjectionPage.Batch.NextCheckpoint.LastEventId);
        Assert.Equal(historyEvent.SourceDocumentId, result.ProjectionPage.Batch.NextCheckpoint.AuditLastSourceDocumentId);
        Assert.Equal(now, result.ProjectionPage.Batch.NextCheckpoint.AuditStartedAtUtc);
        Assert.Null(result.ProjectionPage.Batch.NextCheckpoint.AuditCompletedAtUtc);
        Assert.Equal(0, result.ProjectionPage.Batch.NextCheckpoint.AuditCycle);
    }

    [Fact]
    public async Task Complete_audit_page_records_completion_and_advances_cycle()
    {
        var now = new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
        var identity = ProjectionIdentity.Default();
        var checkpoint = new ProjectionCheckpoint(
            identity,
            AuditLastSourceDocumentId: "66d3b1a4f000000000000001",
            AuditStartedAtUtc: now.AddMinutes(-5),
            AuditCycle: 3);
        var handler = CreateAuditHandler(
            new HistoryAuditResult([], checkpoint.AuditLastSourceDocumentId, true),
            checkpoint,
            now);
        var lease = new ProjectionLeaseToken(identity, "worker-1", 1, now.AddHours(1));

        var result = await handler.PreparePageAsync(
            CreateOptions(identity),
            lease);

        var next = result.ProjectionPage.Batch.NextCheckpoint;
        Assert.True(result.IsComplete);
        Assert.Null(next.AuditLastSourceDocumentId);
        Assert.Equal(checkpoint.AuditStartedAtUtc, next.AuditStartedAtUtc);
        Assert.Equal(now, next.AuditCompletedAtUtc);
        Assert.Equal(4, next.AuditCycle);
    }

    [Fact]
    public async Task Incomplete_audit_page_without_cursor_fails_instead_of_restarting_forever()
    {
        var identity = ProjectionIdentity.Default();
        var eventValue = CreateEvent(DateTimeOffset.UtcNow);
        var handler = CreateAuditHandler(
            new HistoryAuditResult([eventValue], null, false),
            new ProjectionCheckpoint(identity),
            DateTimeOffset.UtcNow);
        var lease = new ProjectionLeaseToken(identity, "worker-1", 1, DateTimeOffset.UtcNow.AddHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.PreparePageAsync(
            CreateOptions(identity),
            lease));
    }

    private static HistoryContractAuditHandler CreateAuditHandler(
        HistoryAuditResult result,
        ProjectionCheckpoint checkpoint,
        DateTimeOffset now) =>
        new(
            new StubAuditReader(result),
            new StubCheckpointStore(checkpoint),
            new IncrementalProjectionHandler(
                new StubHistoryReader(),
                new StubCheckpointStore(checkpoint),
                new ProjectionEventOutcomeMapper(
                    new HistoryEventEligibilityPolicy(),
                    new EventOwnershipPolicy(),
                    new DeviceMetricMapperRegistry([new RawFileMetricMapper()]),
                    new LocalStatisticsDateResolver()),
                new StubMetricKeyResolver(new Dictionary<string, int>
                {
                    ["tag_read"] = 7
                }),
                new StubMetadataResolver(),
                new LocalStatisticsDateResolver(),
                new FixedTimeProvider(now)),
            new FixedTimeProvider(now));

    private static IncrementalProjectionOptions CreateOptions(ProjectionIdentity identity) =>
        new(identity, "v1", 1, DateTimeOffset.UnixEpoch, 10, 20, TimeSpan.Zero, TimeSpan.Zero, [], []);

    private static HistoryEvent CreateEvent(DateTimeOffset persistedAt) => new()
    {
        SourceDocumentId = "66d3b1a4f000000000000001",
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
        ParseStatus = "parsed",
        SourceEventName = "raw_record",
        Facts = new HistoryFacts { TagRead = new TagReadFacts("tag-1", null, null) }
    };

    private sealed class StubAuditReader(HistoryAuditResult result) : IHistoryContractAuditReader
    {
        public Task<HistoryAuditResult> ReadAuditPageAsync(
            string? afterSourceDocumentId,
            int pageSize,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubHistoryReader : IHistoryEventReader
    {
        public Task<HistoryReadResult> ReadPageAsync(
            DateTimeOffset fromPersistedAtUtc,
            DateTimeOffset toPersistedAtUtc,
            SourceCursor? after,
            int pageSize,
            IReadOnlyCollection<long>? companyIds = null,
            IReadOnlyCollection<long>? deviceIds = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HistoryReadResult([], after, toPersistedAtUtc, true));
    }

    private sealed class StubCheckpointStore(ProjectionCheckpoint checkpoint) : IProjectionCheckpointStore
    {
        public Task<ProjectionCheckpoint> GetOrCreateAsync(
            ProjectionIdentity identity,
            CancellationToken cancellationToken = default) => Task.FromResult(checkpoint);

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

    private sealed class StubMetadataResolver : IDeviceMetadataResolver
    {
        public DeviceMetadata? Resolve(HistoryEvent historyEvent) => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
