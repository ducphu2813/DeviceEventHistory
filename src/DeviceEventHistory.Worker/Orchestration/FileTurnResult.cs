namespace DeviceEventHistory.Worker.Orchestration;

public enum FileTurnStatus
{
    CaughtUp = 0,
    WaitingForMoreData = 1,
    Requeue = 2,
    PersistenceFailed = 3,
    CheckpointConflict = 4,
    Truncated = 5,
    Stopped = 6,
    Failed = 7
}

public sealed record FileTurnResult(
    FileTurnStatus Status,
    Exception? Error = null)
{
    public bool ShouldRequeue => Status == FileTurnStatus.Requeue;

    public static FileTurnResult CaughtUp() => new(FileTurnStatus.CaughtUp);

    public static FileTurnResult WaitingForMoreData() => new(FileTurnStatus.WaitingForMoreData);

    public static FileTurnResult Requeue() => new(FileTurnStatus.Requeue);

    public static FileTurnResult PersistenceFailed(Exception error) =>
        new(FileTurnStatus.PersistenceFailed, error);

    public static FileTurnResult CheckpointConflict() =>
        new(FileTurnStatus.CheckpointConflict);

    public static FileTurnResult Truncated() => new(FileTurnStatus.Truncated);

    public static FileTurnResult Stopped(Exception error) =>
        new(FileTurnStatus.Stopped, error);

    public static FileTurnResult Failed(Exception error) =>
        new(FileTurnStatus.Failed, error);
}
