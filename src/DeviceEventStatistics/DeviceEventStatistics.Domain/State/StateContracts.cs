using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Domain.State;

public static class StateTypes
{
    public const string DeviceConnection = "device_connection";
    public const string ScannerConnection = "scanner_connection";

    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.Ordinal)
        {
            DeviceConnection,
            ScannerConnection
        };
}

public static class ConnectionStates
{
    public const string Unknown = "unknown";
    public const string Connected = "connected";
    public const string Disconnected = "disconnected";

    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Unknown,
            Connected,
            Disconnected
        };
}

public static class StateEvidenceKinds
{
    public const string ObservedEvent = "observed_state_event";
    public const string NoPredecessor = "no_predecessor";
    public const string CarriedForward = "carried_forward";
}

public sealed record StateStreamKey(long CompanyId, long DeviceId, string StateType);

public sealed record StateCursorSnapshot(
    StateStreamKey Key,
    string CurrentState,
    DateTimeOffset StateSinceAtUtc,
    DateTimeOffset AccountedThroughAtUtc,
    DateTimeOffset LastTimelineAtUtc,
    string LastEventId,
    string OpeningEvidenceKind);

public sealed record StateBucket(
    DateOnly StatisticsDate,
    DateTimeOffset BucketStartAtUtc,
    DateTimeOffset BucketEndAtUtc,
    string TimeZoneId);

public sealed record StateObservation(
    string EventId,
    StateStreamKey Key,
    string ObservedState,
    DateTimeOffset TimelineAtUtc,
    string OpeningEvidenceKind);

public sealed record StateDailyChange(
    StateStreamKey Key,
    StateBucket Bucket,
    DateTimeOffset CalculatedThroughAtUtc,
    string OpeningState,
    string ClosingState,
    long OnlineSeconds,
    long OfflineSeconds,
    long UnknownSeconds,
    long ConnectedEventCount,
    long DisconnectedEventCount,
    long ReconnectCount,
    string OpeningEvidenceKind,
    string? OpeningEvidenceEventId,
    bool IsDirty,
    bool IsFinalized,
    string CoverageStatus);

public sealed record StateDirtyRange(
    StateStreamKey Key,
    DateOnly FromStatisticsDate,
    DateOnly ToStatisticsDate,
    string ReasonCode,
    string EvidenceEventId);

public sealed record StateCalculationResult(
    StateCursorSnapshot? Cursor,
    IReadOnlyList<StateDailyChange> DailyChanges,
    IReadOnlyList<StateDirtyRange> DirtyRanges,
    bool HasChanges);

public sealed class StateDurationCalculator
{
    public StateCalculationResult Calculate(
        StateCursorSnapshot? cursor,
        IReadOnlyCollection<StateObservation> observations,
        IReadOnlyDictionary<DateOnly, StateBucket> buckets,
        DateTimeOffset asOfAtUtc)
    {
        ValidateInputs(cursor, observations, buckets, asOfAtUtc);

        var ordered = observations
            .OrderBy(value => value.TimelineAtUtc)
            .ThenBy(value => value.EventId, StringComparer.Ordinal)
            .ToArray();

        if (cursor is not null)
        {
            var lateObservation = ordered.FirstOrDefault(value =>
                Compare(value.TimelineAtUtc, value.EventId, cursor.LastTimelineAtUtc, cursor.LastEventId) <= 0 ||
                value.TimelineAtUtc < cursor.AccountedThroughAtUtc);
            if (lateObservation is not null)
            {
                var fromDate = buckets.Keys.Min();
                var toDate = buckets.Keys.Max();
                return new StateCalculationResult(
                    cursor,
                    [],
                    [new StateDirtyRange(
                        cursor.Key,
                        fromDate,
                        toDate,
                        "STAT_STATE_OUT_OF_ORDER",
                        lateObservation.EventId)],
                    false);
            }
        }

        if (cursor is null && ordered.Length == 0)
        {
            return new StateCalculationResult(null, [], [], false);
        }

        var changes = buckets.Values
            .OrderBy(value => value.BucketStartAtUtc)
            .ToDictionary(
                value => value.StatisticsDate,
                value => new DailyAccumulator(
                    cursor?.Key ?? ordered[0].Key,
                    value,
                    cursor?.CurrentState ?? ConnectionStates.Unknown,
                    cursor?.OpeningEvidenceKind ?? StateEvidenceKinds.NoPredecessor),
                EqualityComparer<DateOnly>.Default);

        var currentState = cursor?.CurrentState ?? ConnectionStates.Unknown;
        var currentStateSince = cursor?.StateSinceAtUtc ?? ordered[0].TimelineAtUtc;
        var intervalStart = cursor?.AccountedThroughAtUtc ??
                            FindContainingBucket(buckets.Values, ordered[0].TimelineAtUtc).BucketStartAtUtc;
        var lastTimeline = cursor?.LastTimelineAtUtc ?? intervalStart;
        var lastEventId = cursor?.LastEventId ?? string.Empty;
        var openingEvidenceKind = cursor?.OpeningEvidenceKind ?? StateEvidenceKinds.NoPredecessor;
        var openingEvidenceEventId = (string?)null;

        foreach (var observation in ordered)
        {
            if (observation.TimelineAtUtc > asOfAtUtc)
            {
                break;
            }

            AddInterval(changes, buckets, intervalStart, observation.TimelineAtUtc, currentState);
            var observationBucket = GetBucket(changes, buckets, observation.TimelineAtUtc);
            observationBucket.AddObservation(observation, currentState);

            if (!string.Equals(currentState, observation.ObservedState, StringComparison.Ordinal))
            {
                if (currentState == ConnectionStates.Disconnected &&
                    observation.ObservedState == ConnectionStates.Connected)
                {
                    observationBucket.ReconnectCount++;
                }

                currentState = observation.ObservedState;
                currentStateSince = observation.TimelineAtUtc;
            }

            intervalStart = observation.TimelineAtUtc;
            lastTimeline = observation.TimelineAtUtc;
            lastEventId = observation.EventId;
            openingEvidenceKind = observation.OpeningEvidenceKind;
            openingEvidenceEventId = observation.EventId;
        }

        if (lastEventId.Length == 0 && cursor is null)
        {
            return new StateCalculationResult(null, [], [], false);
        }

        var lastObservation = ordered.LastOrDefault(value => value.TimelineAtUtc <= asOfAtUtc);
        if (lastObservation is not null || cursor is not null)
        {
            var finalBucketEnd = lastObservation is not null
                ? FindContainingBucket(buckets.Values, lastObservation.TimelineAtUtc).BucketEndAtUtc
                : buckets.Values.Max(value => value.BucketEndAtUtc);
            var edge = Min(asOfAtUtc, finalBucketEnd);
            if (edge > intervalStart)
            {
                AddInterval(changes, buckets, intervalStart, edge, currentState);
            }
        }

        var result = changes.Values
            .Where(value => value.HasData)
            .Select(value => value.ToChange(asOfAtUtc, openingEvidenceKind, openingEvidenceEventId))
            .ToArray();

        var nextCursor = new StateCursorSnapshot(
            cursor?.Key ?? ordered[0].Key,
            currentState,
            currentStateSince,
            result.Length == 0
                ? cursor?.AccountedThroughAtUtc ?? intervalStart
                : Max(
                    cursor?.AccountedThroughAtUtc ?? intervalStart,
                    result.Max(value => value.CalculatedThroughAtUtc)),
            lastTimeline,
            lastEventId,
            openingEvidenceKind);

        return new StateCalculationResult(nextCursor, result, [], true);
    }

    private static void ValidateInputs(
        StateCursorSnapshot? cursor,
        IEnumerable<StateObservation> observations,
        IReadOnlyDictionary<DateOnly, StateBucket> buckets,
        DateTimeOffset asOfAtUtc)
    {
        if (buckets.Count == 0)
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_STATE_BUCKET_REQUIRED,
                nameof(buckets));
        }

        if (cursor is not null && !StateTypes.Supported.Contains(cursor.Key.StateType))
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_STATE_CURSOR_TYPE_INVALID,
                nameof(cursor));
        }

        foreach (var observation in observations)
        {
            if (!StateTypes.Supported.Contains(observation.Key.StateType) ||
                !ConnectionStates.Supported.Contains(observation.ObservedState))
            {
                throw new ArgumentException(
                    StatisticsContractConstants.Messages.MSG_STATE_OBSERVATION_INVALID,
                    nameof(observations));
            }

            if (cursor is not null && observation.Key != cursor.Key)
            {
                throw new ArgumentException(
                    StatisticsContractConstants.Messages.MSG_STATE_STREAM_MISMATCH,
                    nameof(observations));
            }
        }

        if (asOfAtUtc < DateTimeOffset.UnixEpoch)
        {
            throw new ArgumentOutOfRangeException(nameof(asOfAtUtc));
        }
    }

    private static void AddInterval(
        IDictionary<DateOnly, DailyAccumulator> changes,
        IReadOnlyDictionary<DateOnly, StateBucket> buckets,
        DateTimeOffset start,
        DateTimeOffset end,
        string state)
    {
        var normalizedStart = FloorToSecond(start);
        var normalizedEnd = FloorToSecond(end);
        if (normalizedEnd <= normalizedStart)
        {
            return;
        }

        foreach (var bucket in buckets.Values.OrderBy(value => value.BucketStartAtUtc))
        {
            var sliceStart = Max(normalizedStart, bucket.BucketStartAtUtc);
            var sliceEnd = Min(normalizedEnd, bucket.BucketEndAtUtc);
            if (sliceEnd <= sliceStart)
            {
                continue;
            }

            var seconds = sliceEnd.ToUnixTimeSeconds() - sliceStart.ToUnixTimeSeconds();
            var accumulator = changes[bucket.StatisticsDate];
            if (!accumulator.HasData &&
                state != ConnectionStates.Unknown &&
                accumulator.OpeningEvidenceKind == StateEvidenceKinds.NoPredecessor)
            {
                accumulator.OpeningEvidenceKind = StateEvidenceKinds.CarriedForward;
            }
            switch (state)
            {
                case ConnectionStates.Connected:
                    accumulator.OnlineSeconds += seconds;
                    break;
                case ConnectionStates.Disconnected:
                    accumulator.OfflineSeconds += seconds;
                    break;
                default:
                    accumulator.UnknownSeconds += seconds;
                    break;
            }

            accumulator.CalculatedThroughAtUtc = Max(accumulator.CalculatedThroughAtUtc, sliceEnd);
            accumulator.HasData = true;
        }
    }

    private static DailyAccumulator GetBucket(
        IDictionary<DateOnly, DailyAccumulator> changes,
        IReadOnlyDictionary<DateOnly, StateBucket> buckets,
        DateTimeOffset timelineAtUtc)
    {
        var containingBucket = buckets.Values.FirstOrDefault(value =>
            timelineAtUtc >= value.BucketStartAtUtc && timelineAtUtc < value.BucketEndAtUtc);
        if (containingBucket is null)
        {
            throw new ArgumentOutOfRangeException(nameof(timelineAtUtc));
        }

        return changes[containingBucket.StatisticsDate];
    }

    private static StateBucket FindContainingBucket(
        IEnumerable<StateBucket> buckets,
        DateTimeOffset timestamp) =>
        buckets.FirstOrDefault(value =>
                   timestamp >= value.BucketStartAtUtc && timestamp < value.BucketEndAtUtc)
               ?? throw new ArgumentOutOfRangeException(nameof(timestamp));

    private static int Compare(DateTimeOffset leftTime, string leftId, DateTimeOffset rightTime, string rightId)
    {
        var timeComparison = leftTime.CompareTo(rightTime);
        return timeComparison != 0
            ? timeComparison
            : string.CompareOrdinal(leftId, rightId);
    }

    private static DateTimeOffset FloorToSecond(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private sealed class DailyAccumulator
    {
        private readonly string initialEvidenceKind;

        public DailyAccumulator(
            StateStreamKey key,
            StateBucket bucket,
            string initialState,
            string initialEvidenceKind)
        {
            Key = key;
            Bucket = bucket;
            CalculatedThroughAtUtc = bucket.BucketStartAtUtc;
            OpeningState = initialState;
            ClosingState = initialState;
            OpeningEvidenceKind = initialEvidenceKind;
            this.initialEvidenceKind = initialEvidenceKind;
        }

        public StateStreamKey Key { get; }
        public StateBucket Bucket { get; }
        public long OnlineSeconds { get; set; }
        public long OfflineSeconds { get; set; }
        public long UnknownSeconds { get; set; }
        public long ConnectedEventCount { get; set; }
        public long DisconnectedEventCount { get; set; }
        public long ReconnectCount { get; set; }
        public DateTimeOffset CalculatedThroughAtUtc { get; set; }
        public string OpeningState { get; set; }
        public string ClosingState { get; set; }
        public string OpeningEvidenceKind { get; set; }
        public string? OpeningEvidenceEventId { get; set; }
        public bool HasData { get; set; }

        public void AddObservation(StateObservation observation, string stateBeforeObservation)
        {
            if (!HasData)
            {
                OpeningState = stateBeforeObservation;
                OpeningEvidenceKind = initialEvidenceKind;
            }

            if (observation.ObservedState == ConnectionStates.Connected)
            {
                ConnectedEventCount++;
            }
            else if (observation.ObservedState == ConnectionStates.Disconnected)
            {
                DisconnectedEventCount++;
            }

            ClosingState = observation.ObservedState;
            HasData = true;
        }

        public StateDailyChange ToChange(
            DateTimeOffset asOfAtUtc,
            string fallbackEvidenceKind,
            string? fallbackEvidenceEventId)
        {
            var through = Min(CalculatedThroughAtUtc, Min(asOfAtUtc, Bucket.BucketEndAtUtc));
            return new StateDailyChange(
                Key,
                Bucket,
                through,
                OpeningState,
                ClosingState,
                OnlineSeconds,
                OfflineSeconds,
                UnknownSeconds,
                ConnectedEventCount,
                DisconnectedEventCount,
                ReconnectCount,
                string.IsNullOrEmpty(OpeningEvidenceKind) ? fallbackEvidenceKind : OpeningEvidenceKind,
                OpeningEvidenceEventId ?? fallbackEvidenceEventId,
                false,
                through == Bucket.BucketEndAtUtc,
                OpeningEvidenceKind == StateEvidenceKinds.NoPredecessor ? "partial" : "complete");
        }
    }
}
