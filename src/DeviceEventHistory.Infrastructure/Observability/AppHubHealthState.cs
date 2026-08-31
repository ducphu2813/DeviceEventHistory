using DeviceEventHistory.Infrastructure.AppHub.Transport;

namespace DeviceEventHistory.Infrastructure.Observability;

public enum AppHubHealthStatus
{
    Connecting = 0,
    Running = 1,
    Degraded = 2,
    Unhealthy = 3
}

public sealed record AppHubSourceHealthSnapshot
{
    public required string SourceId { get; init; }

    public required AppHubConnectionState State { get; init; }

    public required int FailureCount { get; init; }

    public required int ChannelDepth { get; init; }

    public DateTimeOffset? LastCallbackAtUtc { get; init; }

    public DateTimeOffset? LastSuccessfulJoinAtUtc { get; init; }
}

public sealed record AppHubHealthSnapshot
{
    public required AppHubHealthStatus Status { get; init; }

    public required string Reason { get; init; }

    public required int ConfiguredSourceCount { get; init; }

    public required int RunningSourceCount { get; init; }

    public required int ConnectingSourceCount { get; init; }

    public required IReadOnlyCollection<AppHubSourceHealthSnapshot> Sources { get; init; }
}

public sealed class AppHubHealthState
{
    private readonly object sync = new();
    private readonly TimeProvider timeProvider;
    private readonly int failureUnhealthyThreshold;
    private readonly Dictionary<string, SourceState> sources = new(StringComparer.Ordinal);

    public AppHubHealthState(TimeProvider timeProvider, int failureUnhealthyThreshold)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (failureUnhealthyThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failureUnhealthyThreshold));
        }

        this.timeProvider = timeProvider;
        this.failureUnhealthyThreshold = failureUnhealthyThreshold;
    }

    public void ConfigureSources(IEnumerable<string> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);

        lock (sync)
        {
            foreach (var sourceId in sourceIds
                         .Where(sourceId => !string.IsNullOrWhiteSpace(sourceId))
                         .Select(sourceId => sourceId.Trim()))
            {
                if (!sources.ContainsKey(sourceId))
                {
                    sources[sourceId] = new SourceState();
                }
            }
        }
    }

    public void MarkConnectionState(string sourceId, AppHubConnectionState state)
    {
        Update(sourceId, source =>
        {
            source.State = state;
            if (state == AppHubConnectionState.Running)
            {
                source.FailureCount = 0;
                source.LastSuccessfulJoinAtUtc = timeProvider.GetUtcNow();
            }
        });
    }

    public void MarkConnectionFailure(string sourceId)
    {
        Update(sourceId, source =>
        {
            source.State = AppHubConnectionState.Disconnected;
            source.FailureCount++;
        });
    }

    public void RecordCallbackReceived(string sourceId) =>
        Update(sourceId, source => source.LastCallbackAtUtc = timeProvider.GetUtcNow());

    public void SetChannelDepth(string sourceId, int depth)
    {
        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        Update(sourceId, source => source.ChannelDepth = depth);
    }

    public AppHubHealthSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                var sourceSnapshots = sources
                    .Select(pair => pair.Value.ToSnapshot(pair.Key))
                    .ToArray();
                var runningSourceCount = sourceSnapshots.Count(source =>
                    source.State == AppHubConnectionState.Running);
                var connectingSourceCount = sourceSnapshots.Count(source =>
                    source.State is AppHubConnectionState.Connecting or
                        AppHubConnectionState.Connected or
                        AppHubConnectionState.JoiningMonitoring or
                        AppHubConnectionState.Reconnecting);
                var status = GetStatus(sourceSnapshots, runningSourceCount);

                return new AppHubHealthSnapshot
                {
                    Status = status,
                    Reason = GetReason(status),
                    ConfiguredSourceCount = sourceSnapshots.Length,
                    RunningSourceCount = runningSourceCount,
                    ConnectingSourceCount = connectingSourceCount,
                    Sources = sourceSnapshots
                };
            }
        }
    }

    private AppHubHealthStatus GetStatus(
        IReadOnlyCollection<AppHubSourceHealthSnapshot> sourceSnapshots,
        int runningSourceCount)
    {
        if (sourceSnapshots.Count == 0)
        {
            return AppHubHealthStatus.Unhealthy;
        }

        if (sourceSnapshots.All(source =>
                source.FailureCount >= failureUnhealthyThreshold))
        {
            return AppHubHealthStatus.Unhealthy;
        }

        if (runningSourceCount == sourceSnapshots.Count)
        {
            return AppHubHealthStatus.Running;
        }

        if (runningSourceCount == 0 && sourceSnapshots.All(source =>
                source.State is AppHubConnectionState.Disconnected or
                    AppHubConnectionState.Connecting or
                    AppHubConnectionState.Connected or
                    AppHubConnectionState.JoiningMonitoring or
                    AppHubConnectionState.Reconnecting))
        {
            return AppHubHealthStatus.Connecting;
        }

        return AppHubHealthStatus.Degraded;
    }

    private static string GetReason(AppHubHealthStatus status) => status switch
    {
        AppHubHealthStatus.Connecting => Domain.Common.AppConst.Observability.HealthReasonAppHubConnecting,
        AppHubHealthStatus.Running => Domain.Common.AppConst.Observability.HealthStatusReady,
        AppHubHealthStatus.Degraded => Domain.Common.AppConst.Observability.HealthReasonAppHubDegraded,
        AppHubHealthStatus.Unhealthy => Domain.Common.AppConst.Observability.HealthReasonAppHubUnavailable,
        _ => Domain.Common.AppConst.Observability.HealthReasonAppHubUnavailable
    };

    private void Update(string sourceId, Action<SourceState> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(update);

        lock (sync)
        {
            var normalizedSourceId = sourceId.Trim();
            if (!sources.TryGetValue(normalizedSourceId, out var source))
            {
                source = new SourceState();
                sources[normalizedSourceId] = source;
            }

            update(source);
        }
    }

    private sealed class SourceState
    {
        public AppHubConnectionState State { get; set; } = AppHubConnectionState.Disconnected;

        public int FailureCount { get; set; }

        public int ChannelDepth { get; set; }

        public DateTimeOffset? LastCallbackAtUtc { get; set; }

        public DateTimeOffset? LastSuccessfulJoinAtUtc { get; set; }

        public AppHubSourceHealthSnapshot ToSnapshot(string sourceId) => new()
        {
            SourceId = sourceId,
            State = State,
            FailureCount = FailureCount,
            ChannelDepth = ChannelDepth,
            LastCallbackAtUtc = LastCallbackAtUtc,
            LastSuccessfulJoinAtUtc = LastSuccessfulJoinAtUtc
        };
    }
}
