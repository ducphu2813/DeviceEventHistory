using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;

namespace DeviceEventStatistics.UnitTests;

public sealed class StatisticsProjectionPolicyTests
{
    [Fact]
    public void Eligibility_accepts_parsed_with_warnings_when_core_identity_is_valid()
    {
        var historyEvent = CreateEvent(parseStatus: "parsed_with_warnings");

        var decision = new HistoryEventEligibilityPolicy().Evaluate(historyEvent);

        Assert.Equal(ProjectionEventDisposition.Aggregated, decision.Disposition);
    }

    [Fact]
    public void Eligibility_routes_unmapped_event_to_quality_only()
    {
        var historyEvent = CreateEvent(parseStatus: "unmapped");

        var decision = new HistoryEventEligibilityPolicy().Evaluate(historyEvent);

        Assert.Equal(ProjectionEventDisposition.QualityOnly, decision.Disposition);
    }

    [Fact]
    public void Ownership_ignores_apphub_tag_read_as_secondary_source()
    {
        var historyEvent = CreateEvent(
            sourceKind: "erp_apphub",
            category: "tag_read");

        var decision = new EventOwnershipPolicy().Evaluate(historyEvent);

        Assert.Equal(ProjectionEventDisposition.Ignored, decision.Disposition);
        Assert.Equal("secondary_tag_source", decision.ReasonCode);
    }

    [Fact]
    public void Mapper_registry_rejects_duplicate_keys()
    {
        var mapper = new RawFileMetricMapper();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DeviceMetricMapperRegistry([mapper, mapper]));

        Assert.Contains("STAT-METRIC-MAPPER-DUPLICATE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Vietnam_bucket_uses_fixed_utc_plus_seven_boundary()
    {
        var resolver = new DeviceEventStatistics.Application.Time.LocalStatisticsDateResolver();

        var beforeMidnight = resolver.Resolve(new DateTimeOffset(2026, 8, 28, 16, 59, 59, TimeSpan.Zero));
        var afterMidnight = resolver.Resolve(new DateTimeOffset(2026, 8, 28, 17, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 8, 28), beforeMidnight.StatisticsDate);
        Assert.Equal(new DateOnly(2026, 8, 29), afterMidnight.StatisticsDate);
        Assert.Equal(TimeSpan.FromHours(24), afterMidnight.BucketEndAtUtc - afterMidnight.BucketStartAtUtc);
    }

    [Fact]
    public void Source_cursor_is_strictly_ordered_by_persisted_time_then_event_id()
    {
        var current = new SourceCursor(DateTimeOffset.UnixEpoch, "10".PadLeft(64, '0'));

        Assert.False(new SourceCursor(DateTimeOffset.UnixEpoch, "0f".PadLeft(64, '0')).IsAfter(current));
        Assert.True(new SourceCursor(DateTimeOffset.UnixEpoch, "11".PadLeft(64, '0')).IsAfter(current));
        Assert.True(new SourceCursor(DateTimeOffset.UnixEpoch.AddTicks(1), current.EventId).IsAfter(current));
    }

    [Fact]
    public void Metric_registry_maps_raw_tag_and_apphub_connection_to_stable_codes()
    {
        var registry = new DeviceMetricMapperRegistry(
        [
            new RawFileMetricMapper(),
            new AppHubConnectionMetricMapper(),
            new AppHubDeviceOnlineMetricMapper(),
            new AppHubControlMetricMapper(),
            new AppHubSensorMetricMapper(),
            new AppHubScannerMetricMapper()
        ]);
        var timeline = new DateTimeOffset(2026, 8, 28, 8, 30, 0, TimeSpan.Zero);
        var bucket = new LocalStatisticsDateResolver().Resolve(timeline);

        var tagEvent = CreateEvent() with
        {
            SourceEventName = "raw_record",
            Facts = new HistoryFacts { TagRead = new TagReadFacts("tag-1", null, null) }
        };
        var connectionEvent = CreateEvent(
            sourceKind: "erp_apphub",
            category: "device_connection") with
        {
            SourceEventName = "receiveStateConnected",
            Facts = new HistoryFacts
            {
                Connection = new ConnectionFacts("connected", false, true, true)
            }
        };

        Assert.True(registry.TryMap(tagEvent, bucket, out var tagMetrics));
        Assert.Equal("tag_read", Assert.Single(tagMetrics).MetricCode);
        Assert.True(registry.TryMap(connectionEvent, bucket, out var connectionMetrics));
        Assert.Equal("device_connected", Assert.Single(connectionMetrics).MetricCode);
    }

    [Fact]
    public void Outcome_mapper_keeps_warning_and_received_time_quality_without_losing_metric()
    {
        var registry = new DeviceMetricMapperRegistry([new RawFileMetricMapper()]);
        var mapper = new ProjectionEventOutcomeMapper(
            new HistoryEventEligibilityPolicy(),
            new EventOwnershipPolicy(),
            registry,
            new LocalStatisticsDateResolver());
        var historyEvent = CreateEvent() with
        {
            SourceEventName = "raw_record",
            TimeBasis = "received",
            ParseStatus = "parsed_with_warnings",
            Facts = new HistoryFacts { TagRead = new TagReadFacts("tag-1", null, null) }
        };

        var outcome = mapper.Map(historyEvent);

        Assert.Equal(ProjectionEventDisposition.Aggregated, outcome.Disposition);
        Assert.Equal("tag_read", Assert.Single(outcome.Metrics).MetricCode);
        Assert.Contains(outcome.Quality, item => item.QualityCode == "parsed_with_warnings");
        Assert.Contains(outcome.Quality, item => item.QualityCode == "received_time_basis");
    }

    [Fact]
    public void Sweep_replay_keeps_fixed_upper_bound_and_advances_page_cursor()
    {
        var identity = ProjectionIdentity.Default();
        var checkpoint = new ProjectionCheckpoint(identity, DateTimeOffset.UnixEpoch, new string('a', 64));
        var now = DateTimeOffset.UnixEpoch.AddHours(2);

        var sweep = ProjectionSweep.Start(
            checkpoint,
            now,
            DateTimeOffset.UnixEpoch.AddDays(-1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30));
        var pageCursor = new SourceCursor(DateTimeOffset.UnixEpoch.AddHours(1), new string('b', 64));
        var next = sweep.ApplyPage(checkpoint, pageCursor, 500, pageIsComplete: false, now);

        Assert.Equal(now.AddSeconds(-30), sweep.ToAtUtc);
        Assert.Equal(pageCursor.EventId, next.SweepLastEventId);
        Assert.Equal(sweep.FromAtUtc, next.SweepFromAtUtc);
        Assert.Equal(checkpoint.LastPersistedAtUtc, next.LastPersistedAtUtc);
    }

    private static HistoryEvent CreateEvent(
        string parseStatus = "parsed",
        string sourceKind = "rfid_antenna_file",
        string category = "tag_read") =>
        new()
        {
            SourceDocumentId = "source-document-1",
            EventId = new string('a', 64),
            SchemaVersion = 1,
            CompanyId = 2,
            Category = category,
            SourceKind = sourceKind,
            SourceId = "source-1",
            PersistedAtUtc = DateTimeOffset.UtcNow,
            TimelineAtUtc = DateTimeOffset.UtcNow,
            TimeBasis = "occurred",
            DeviceId = 101,
            ParseStatus = parseStatus
        };
}
