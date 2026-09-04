using DeviceEventStatistics.Domain.State;

namespace DeviceEventStatistics.UnitTests;

public sealed class StateDurationCalculatorTests
{
    private static readonly StateStreamKey Stream = new(2, 101, StateTypes.DeviceConnection);
    private static readonly StateDurationCalculator Calculator = new();

    [Fact]
    public void Repeated_connected_observations_do_not_open_a_second_interval()
    {
        var bucket = Bucket(new DateOnly(2026, 8, 28));
        var observations = new[]
        {
            Observation("a", "connected", Utc(2026, 8, 28, 1)),
            Observation("b", "connected", Utc(2026, 8, 28, 3)),
            Observation("c", "disconnected", Utc(2026, 8, 28, 4))
        };

        var result = Calculator.Calculate(
            null,
            observations,
            new Dictionary<DateOnly, StateBucket> { [bucket.StatisticsDate] = bucket },
            Utc(2026, 8, 28, 5));
        var daily = Assert.Single(result.DailyChanges);

        Assert.Equal(10_800, daily.OnlineSeconds);
        Assert.Equal(3_600, daily.OfflineSeconds);
        Assert.Equal(2, daily.ConnectedEventCount);
        Assert.Equal(1, daily.DisconnectedEventCount);
        Assert.Equal(0, daily.ReconnectCount);
    }

    [Fact]
    public void Interval_is_split_at_the_business_midnight_boundary()
    {
        var firstBucket = Bucket(new DateOnly(2026, 8, 28));
        var secondBucket = Bucket(new DateOnly(2026, 8, 29));
        var observations = new[]
        {
            Observation("a", "connected", Utc(2026, 8, 28, 16)),
            Observation("b", "disconnected", Utc(2026, 8, 28, 18))
        };

        var result = Calculator.Calculate(
            null,
            observations,
            new[] { firstBucket, secondBucket }.ToDictionary(value => value.StatisticsDate),
            Utc(2026, 8, 29, 3));

        Assert.Equal(3_600, Assert.Single(result.DailyChanges, value => value.Bucket.StatisticsDate == firstBucket.StatisticsDate).OnlineSeconds);
        Assert.Equal(3_600, Assert.Single(result.DailyChanges, value => value.Bucket.StatisticsDate == secondBucket.StatisticsDate).OnlineSeconds);
    }

    [Fact]
    public void Late_observation_marks_the_range_dirty_without_negative_duration()
    {
        var bucket = Bucket(new DateOnly(2026, 8, 28));
        var cursor = new StateCursorSnapshot(
            Stream,
            ConnectionStates.Connected,
            Utc(2026, 8, 28, 1),
            Utc(2026, 8, 28, 5),
            Utc(2026, 8, 28, 1),
            "a",
            StateEvidenceKinds.ObservedEvent);

        var result = Calculator.Calculate(
            cursor,
            [Observation("b", "disconnected", Utc(2026, 8, 28, 3))],
            new Dictionary<DateOnly, StateBucket> { [bucket.StatisticsDate] = bucket },
            Utc(2026, 8, 28, 6));

        Assert.Empty(result.DailyChanges);
        Assert.Single(result.DirtyRanges);
        Assert.Equal("STAT_STATE_OUT_OF_ORDER", result.DirtyRanges[0].ReasonCode);
    }

    [Fact]
    public void Refresh_does_not_recount_an_already_accounted_edge()
    {
        var bucket = Bucket(new DateOnly(2026, 8, 28));
        var cursor = new StateCursorSnapshot(
            Stream,
            ConnectionStates.Connected,
            Utc(2026, 8, 28, 1),
            Utc(2026, 8, 28, 2),
            Utc(2026, 8, 28, 1),
            "a",
            StateEvidenceKinds.ObservedEvent);
        var buckets = new Dictionary<DateOnly, StateBucket> { [bucket.StatisticsDate] = bucket };

        var first = Calculator.Calculate(cursor, [], buckets, Utc(2026, 8, 28, 4));
        var second = Calculator.Calculate(first.Cursor, [], buckets, Utc(2026, 8, 28, 4));

        Assert.Equal(7_200, Assert.Single(first.DailyChanges).OnlineSeconds);
        Assert.Empty(second.DailyChanges);
    }

    private static StateObservation Observation(string eventId, string state, DateTimeOffset timeline) =>
        new(eventId, Stream, state, timeline, StateEvidenceKinds.ObservedEvent);

    private static StateBucket Bucket(DateOnly date) =>
        new(
            date,
            date.AddDays(-1).ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc),
            date.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc),
            "Asia/Ho_Chi_Minh");

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
