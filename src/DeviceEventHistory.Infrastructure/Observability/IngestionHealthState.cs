using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.Observability;

public enum IngestionHealthStatus
{
    Live = 0,
    Ready = 1,
    Degraded = 2,
    Unhealthy = 3
}

public sealed record IngestionFileHealthSnapshot
{
    public required string SourceId { get; init; }

    public required long FileId { get; init; }

    public required bool Active { get; init; }

    public required long CheckpointPosition { get; init; }

    public long? FileLength { get; init; }

    public required int PendingBytes { get; init; }

    public required DateTimeOffset LastReadAtUtc { get; init; }

    public required DateTimeOffset LastCheckpointAtUtc { get; init; }

    public required string LastResult { get; init; }

    public required bool IsTruncated { get; init; }
}

public sealed record IngestionHealthSnapshot
{
    public required bool IsLive { get; init; }

    public required bool StartupReady { get; init; }

    public required IngestionHealthStatus Status { get; init; }

    public required string Reason { get; init; }

    public required int ConfiguredSourceCount { get; init; }

    public required int AvailableSourceCount { get; init; }

    public required int ActiveFileCount { get; init; }

    public required int MongoFailureCount { get; init; }

    public required IReadOnlyCollection<IngestionFileHealthSnapshot> Files { get; init; }
}

public sealed class IngestionHealthState
{
    private readonly object sync = new();
    private readonly TimeProvider timeProvider;
    private readonly int mongoFailureUnhealthyThreshold;
    private readonly int sourceFailureUnhealthyThreshold;
    private readonly TimeSpan progressStaleAfter;
    private readonly Dictionary<string, SourceState> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileState> files = new(StringComparer.Ordinal);
    private bool isLive;
    private bool startupReady;
    private int mongoFailureCount;

    public IngestionHealthState(
        TimeProvider timeProvider,
        int mongoFailureUnhealthyThreshold,
        int sourceFailureUnhealthyThreshold,
        TimeSpan progressStaleAfter)
    {
        this.timeProvider = timeProvider;
        this.mongoFailureUnhealthyThreshold = mongoFailureUnhealthyThreshold;
        this.sourceFailureUnhealthyThreshold = sourceFailureUnhealthyThreshold;
        this.progressStaleAfter = progressStaleAfter;
    }

    public void ConfigureSources(IEnumerable<string> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);

        lock (sync)
        {
            foreach (var sourceId in sourceIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (!sources.ContainsKey(sourceId))
                {
                    sources[sourceId] = new SourceState();
                }
            }
        }
    }

    public void MarkLive() => Update(() => isLive = true);

    public void MarkStopped() => Update(() => isLive = false);

    public void MarkStartupReady() => Update(() => startupReady = true);

    public void MarkMongoAvailable() => Update(() => mongoFailureCount = 0);

    public void MarkMongoUnavailable() => Update(() => mongoFailureCount++);

    public void MarkSourceAvailable(string sourceId)
    {
        Update(() => GetSource(sourceId).MarkAvailable());
    }

    public void MarkSourceUnavailable(string sourceId)
    {
        Update(() => GetSource(sourceId).MarkUnavailable());
    }

    public void MarkFileProcessingStarted(string sourceId, long fileId)
    {
        Update(() => GetFile(sourceId, fileId).Active = true);
    }

    public void MarkFileProcessingCompleted(
        string sourceId,
        long fileId,
        string result)
    {
        Update(() =>
        {
            var file = GetFile(sourceId, fileId);
            file.Active = false;
            file.LastResult = result;
        });
    }

    public void RecordProgress(
        string sourceId,
        long fileId,
        long checkpointPosition,
        long? fileLength,
        int pendingBytes,
        DateTimeOffset? checkpointUpdatedAtUtc)
    {
        Update(() =>
        {
            var file = GetFile(sourceId, fileId);
            var now = timeProvider.GetUtcNow();
            file.CheckpointPosition = checkpointPosition;
            file.FileLength = fileLength;
            file.PendingBytes = pendingBytes;
            file.LastReadAtUtc = now;
            if (checkpointUpdatedAtUtc.HasValue)
            {
                file.LastCheckpointAtUtc = checkpointUpdatedAtUtc.Value;
            }
        });
    }

    public void RecordCheckpointAdvance(
        string sourceId,
        long fileId,
        long position,
        bool succeeded)
    {
        if (!succeeded)
        {
            return;
        }

        Update(() =>
        {
            var file = GetFile(sourceId, fileId);
            file.CheckpointPosition = position;
            file.LastCheckpointAtUtc = timeProvider.GetUtcNow();
        });
    }

    public void MarkFileTruncated(string sourceId, long fileId)
    {
        Update(() => GetFile(sourceId, fileId).IsTruncated = true);
    }

    public IngestionHealthSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                var now = timeProvider.GetUtcNow();
                var fileSnapshots = files.Values.Select(file => file.ToSnapshot()).ToArray();
                var availableSources = sources.Values.Count(source => source.IsAvailable);
                var status = GetStatus(now, fileSnapshots, availableSources);

                return new IngestionHealthSnapshot
                {
                    IsLive = isLive,
                    StartupReady = startupReady,
                    Status = status,
                    Reason = GetReason(status, fileSnapshots),
                    ConfiguredSourceCount = sources.Count,
                    AvailableSourceCount = availableSources,
                    ActiveFileCount = fileSnapshots.Count(file => file.Active),
                    MongoFailureCount = mongoFailureCount,
                    Files = fileSnapshots
                };
            }
        }
    }

    private IngestionHealthStatus GetStatus(
        DateTimeOffset now,
        IReadOnlyCollection<IngestionFileHealthSnapshot> fileSnapshots,
        int availableSourceCount)
    {
        if (fileSnapshots.Any(file => file.IsTruncated) ||
            mongoFailureCount >= mongoFailureUnhealthyThreshold ||
            (sources.Count > 0 &&
             availableSourceCount == 0 &&
             sources.Values.All(source => source.FailureCount >= sourceFailureUnhealthyThreshold)))
        {
            return IngestionHealthStatus.Unhealthy;
        }

        if (!startupReady ||
            mongoFailureCount > 0 ||
            sources.Values.Any(source => !source.IsAvailable && source.FailureCount > 0) ||
            fileSnapshots.Any(file => IsStale(file, now)))
        {
            return IngestionHealthStatus.Degraded;
        }

        return isLive ? IngestionHealthStatus.Ready : IngestionHealthStatus.Live;
    }

    private string GetReason(
        IngestionHealthStatus status,
        IReadOnlyCollection<IngestionFileHealthSnapshot> fileSnapshots) =>
        status switch
        {
            IngestionHealthStatus.Unhealthy when fileSnapshots.Any(file => file.IsTruncated) =>
                AppConst.Observability.HealthReasonFileTruncated,
            IngestionHealthStatus.Unhealthy when mongoFailureCount >= mongoFailureUnhealthyThreshold =>
                AppConst.Observability.HealthReasonMongoUnavailable,
            IngestionHealthStatus.Unhealthy => AppConst.Observability.HealthReasonSourceUnavailable,
            IngestionHealthStatus.Degraded when !startupReady =>
                AppConst.Observability.HealthReasonStartupPending,
            IngestionHealthStatus.Degraded when mongoFailureCount > 0 =>
                AppConst.Observability.HealthReasonMongoUnavailable,
            IngestionHealthStatus.Degraded when fileSnapshots.Any(IsStale) =>
                AppConst.Observability.HealthReasonProgressStale,
            IngestionHealthStatus.Degraded => AppConst.Observability.HealthReasonSourceUnavailable,
            _ => AppConst.Observability.HealthStatusReady
        };

    private bool IsStale(IngestionFileHealthSnapshot file) =>
        IsStale(file, timeProvider.GetUtcNow());

    private bool IsStale(IngestionFileHealthSnapshot file, DateTimeOffset now) =>
        file.FileLength.HasValue &&
        file.FileLength.Value > file.CheckpointPosition &&
        now - file.LastCheckpointAtUtc >= progressStaleAfter;

    private SourceState GetSource(string sourceId)
    {
        if (!sources.TryGetValue(sourceId, out var source))
        {
            source = new SourceState();
            sources[sourceId] = source;
        }

        return source;
    }

    private FileState GetFile(string sourceId, long fileId)
    {
        var key = $"{sourceId}|{fileId}";
        if (!files.TryGetValue(key, out var file))
        {
            file = new FileState(sourceId, fileId, timeProvider.GetUtcNow());
            files[key] = file;
        }

        return file;
    }

    private void Update(Action action)
    {
        lock (sync)
        {
            action();
        }
    }

    private sealed class SourceState
    {
        public bool IsAvailable { get; private set; }

        public int FailureCount { get; private set; }

        public void MarkAvailable()
        {
            IsAvailable = true;
            FailureCount = 0;
        }

        public void MarkUnavailable()
        {
            IsAvailable = false;
            FailureCount++;
        }
    }

    private sealed class FileState(
        string sourceId,
        long fileId,
        DateTimeOffset now)
    {
        public string SourceId { get; } = sourceId;

        public long FileId { get; } = fileId;

        public bool Active { get; set; }

        public long CheckpointPosition { get; set; }

        public long? FileLength { get; set; }

        public int PendingBytes { get; set; }

        public DateTimeOffset LastReadAtUtc { get; set; } = now;

        public DateTimeOffset LastCheckpointAtUtc { get; set; } = now;

        public string LastResult { get; set; } = AppConst.Observability.HealthStatusReady;

        public bool IsTruncated { get; set; }

        public IngestionFileHealthSnapshot ToSnapshot() => new()
        {
            SourceId = SourceId,
            FileId = FileId,
            Active = Active,
            CheckpointPosition = CheckpointPosition,
            FileLength = FileLength,
            PendingBytes = PendingBytes,
            LastReadAtUtc = LastReadAtUtc,
            LastCheckpointAtUtc = LastCheckpointAtUtc,
            LastResult = LastResult,
            IsTruncated = IsTruncated
        };
    }
}
