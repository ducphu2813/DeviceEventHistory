using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Observability;

public interface IStatisticsTelemetry
{
    void RecordBatchCommitted(
        string mode,
        ProjectionPageResult result,
        TimeSpan duration);

    void RecordBatchFailed(string mode);

    void RecordLeaseTransition(string transition);

    void RecordReconciliation(
        ReconciliationRunResult result,
        TimeSpan duration);

    void RecordOperationalCleanup(int deletedStagingRows, int deletedProjectionRuns);
}

public sealed class NullStatisticsTelemetry : IStatisticsTelemetry
{
    public void RecordBatchCommitted(string mode, ProjectionPageResult result, TimeSpan duration)
    {
    }

    public void RecordBatchFailed(string mode)
    {
    }

    public void RecordLeaseTransition(string transition)
    {
    }

    public void RecordReconciliation(ReconciliationRunResult result, TimeSpan duration)
    {
    }

    public void RecordOperationalCleanup(int deletedStagingRows, int deletedProjectionRuns)
    {
    }
}

public sealed record ProjectionOperationalSnapshot(
    DateTimeOffset? SourceLatestPersistedAtUtc,
    DateTimeOffset? SourceOldestPersistedAtUtc,
    DateTimeOffset? CheckpointLastPersistedAtUtc,
    bool LeaseHeld,
    int PendingRequestCount,
    DateTimeOffset? OldestPendingRequestAtUtc,
    DateTimeOffset? LastDurationRefreshAtUtc,
    DateTimeOffset? LastSuccessfulRunAtUtc,
    bool HasUnrecoverableCoverage);

public interface IProjectionOperationalSnapshotReader
{
    Task<ProjectionOperationalSnapshot> ReadAsync(
        ProjectionIdentity identity,
        string owner,
        CancellationToken cancellationToken = default);
}

public enum StatisticsHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}

public sealed record StatisticsHealthEvaluation(
    StatisticsHealthStatus Status,
    string Reason,
    TimeSpan? IncrementalLag,
    TimeSpan? PendingRequestAge,
    TimeSpan? RetentionHeadroom);

public sealed record StatisticsHealthInput(
    bool StartupReady,
    bool DependenciesAvailable,
    bool IsDraining,
    DateTimeOffset NowUtc,
    ProjectionOperationalSnapshot OperationalSnapshot);

public sealed class StatisticsHealthEvaluator
{
    public StatisticsHealthEvaluation Evaluate(
        StatisticsHealthInput input,
        TimeSpan lagWarningAfter,
        TimeSpan lagViolationAfter)
    {
        var snapshot = input.OperationalSnapshot;
        var lag = CalculateLag(
            snapshot.SourceLatestPersistedAtUtc,
            snapshot.CheckpointLastPersistedAtUtc);
        var pendingAge = input.NowUtc - snapshot.OldestPendingRequestAtUtc;
        var retentionHeadroom = CalculateRetentionHeadroom(
            input.NowUtc,
            snapshot.SourceOldestPersistedAtUtc,
            snapshot.OldestPendingRequestAtUtc);

        if (!input.StartupReady || !input.DependenciesAvailable)
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.StartupOrDependencyFailure, lag, pendingAge, retentionHeadroom);
        }

        if (input.IsDraining)
        {
            return Evaluation(StatisticsHealthStatus.Degraded, StatisticsContractConstants.HealthReasons.Draining, lag, pendingAge, retentionHeadroom);
        }

        if (!snapshot.LeaseHeld)
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.LeaseNotHeld, lag, pendingAge, retentionHeadroom);
        }

        if (snapshot.HasUnrecoverableCoverage)
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.UnrecoverableCoverage, lag, pendingAge, retentionHeadroom);
        }

        if (IsAtOrAbove(lag, lagViolationAfter) || IsAtOrAbove(pendingAge, lagViolationAfter))
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.LagSloBreached, lag, pendingAge, retentionHeadroom);
        }

        if (IsAtOrAbove(lag, lagWarningAfter) || IsAtOrAbove(pendingAge, lagWarningAfter) ||
            retentionHeadroom is not null && retentionHeadroom <= TimeSpan.Zero)
        {
            return Evaluation(StatisticsHealthStatus.Degraded, StatisticsContractConstants.HealthReasons.LagOrRetentionWarning, lag, pendingAge, retentionHeadroom);
        }

        return Evaluation(StatisticsHealthStatus.Healthy, StatisticsContractConstants.HealthReasons.CaughtUp, lag, pendingAge, retentionHeadroom);
    }

    private static TimeSpan? CalculateLag(
        DateTimeOffset? sourceLatestPersistedAtUtc,
        DateTimeOffset? checkpointLastPersistedAtUtc)
    {
        if (sourceLatestPersistedAtUtc is not DateTimeOffset sourceLatest)
        {
            return null;
        }

        if (checkpointLastPersistedAtUtc is not DateTimeOffset checkpoint)
        {
            return TimeSpan.MaxValue;
        }

        return sourceLatest > checkpoint
            ? sourceLatest - checkpoint
            : TimeSpan.Zero;
    }

    private static TimeSpan? CalculateRetentionHeadroom(
        DateTimeOffset nowUtc,
        DateTimeOffset? sourceOldestPersistedAtUtc,
        DateTimeOffset? oldestPendingRequestAtUtc)
    {
        if (sourceOldestPersistedAtUtc is not DateTimeOffset sourceOldest ||
            oldestPendingRequestAtUtc is not DateTimeOffset pending)
        {
            return null;
        }

        return sourceOldest - pending;
    }

    private static bool IsAtOrAbove(TimeSpan? value, TimeSpan threshold) =>
        value is TimeSpan actual && actual >= threshold;

    private static StatisticsHealthEvaluation Evaluation(
        StatisticsHealthStatus status,
        string reason,
        TimeSpan? lag,
        TimeSpan? pendingAge,
        TimeSpan? retentionHeadroom) =>
        new(status, reason, lag, pendingAge, retentionHeadroom);
}
