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

    void RecordHealthSnapshot(
        ProjectionOperationalSnapshot snapshot,
        StatisticsHealthEvaluation evaluation);
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

    public void RecordHealthSnapshot(
        ProjectionOperationalSnapshot snapshot,
        StatisticsHealthEvaluation evaluation)
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
    bool HasUnrecoverableCoverage,
    DateTimeOffset? OldestPendingRequiredFromAtUtc = null,
    DateTimeOffset? RetentionBoundaryAtUtc = null,
    DateTimeOffset? AuditStartedAtUtc = null,
    DateTimeOffset? AuditCompletedAtUtc = null,
    int CoverageGapCount = 0);

public interface IProjectionOperationalSnapshotReader
{
    Task<ProjectionOperationalSnapshot> ReadAsync(
        ProjectionIdentity identity,
        string owner,
        CancellationToken cancellationToken = default);

    Task<ProjectionOperationalSnapshot> ReadAsync(
        ProjectionIdentity identity,
        string owner,
        DateTimeOffset? retentionBoundaryAtUtc,
        CancellationToken cancellationToken = default) =>
        ReadAsync(identity, owner, cancellationToken);
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
    TimeSpan? RetentionHeadroom,
    int PendingRequestCount = 0,
    int CoverageGapCount = 0,
    TimeSpan? AuditCursorAge = null);

public sealed record StatisticsHealthInput(
    bool StartupReady,
    bool DependenciesAvailable,
    bool IsDraining,
    DateTimeOffset NowUtc,
    ProjectionOperationalSnapshot OperationalSnapshot,
    bool RequiresLease = true);

public sealed class StatisticsHealthEvaluator
{
    public StatisticsHealthEvaluation Evaluate(
        StatisticsHealthInput input,
        TimeSpan lagWarningAfter,
        TimeSpan lagViolationAfter,
        TimeSpan minimumHistoryHeadroom = default)
    {
        var snapshot = input.OperationalSnapshot;
        var lag = CalculateLag(
            snapshot.SourceLatestPersistedAtUtc,
            snapshot.CheckpointLastPersistedAtUtc);
        var pendingAge = CalculateAge(input.NowUtc, snapshot.OldestPendingRequestAtUtc);
        var retentionHeadroom = CalculateRetentionHeadroom(
            snapshot.OldestPendingRequiredFromAtUtc,
            snapshot.RetentionBoundaryAtUtc);
        var unrecoverableCoverage = snapshot.HasUnrecoverableCoverage ||
            IsSourceCoverageMissing(
                snapshot.SourceOldestPersistedAtUtc,
                snapshot.OldestPendingRequiredFromAtUtc);
        var sourceRetentionRisk = retentionHeadroom is TimeSpan headroom && headroom < TimeSpan.Zero;
        var auditCursorAge = CalculateAge(
            input.NowUtc,
            snapshot.AuditCompletedAtUtc ?? snapshot.AuditStartedAtUtc);

        if (!input.StartupReady || !input.DependenciesAvailable)
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.StartupOrDependencyFailure, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        if (input.IsDraining)
        {
            return Evaluation(StatisticsHealthStatus.Degraded, StatisticsContractConstants.HealthReasons.Draining, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        if (input.RequiresLease && !snapshot.LeaseHeld)
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.LeaseNotHeld, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        if (unrecoverableCoverage)
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.UnrecoverableCoverage, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        if (sourceRetentionRisk)
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.SourceRetentionRisk, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        if (IsAtOrAbove(lag, lagViolationAfter))
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.LagSloBreached, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        if (IsAtOrAbove(pendingAge, lagViolationAfter))
        {
            return Evaluation(StatisticsHealthStatus.Unhealthy, StatisticsContractConstants.HealthReasons.PendingRequestAge, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        if (IsAtOrAbove(lag, lagWarningAfter) ||
            IsAtOrAbove(pendingAge, lagWarningAfter) ||
            IsAtOrBelow(retentionHeadroom, minimumHistoryHeadroom))
        {
            return Evaluation(StatisticsHealthStatus.Degraded, StatisticsContractConstants.HealthReasons.LagOrRetentionWarning, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
        }

        return Evaluation(StatisticsHealthStatus.Healthy, StatisticsContractConstants.HealthReasons.CaughtUp, lag, pendingAge, retentionHeadroom, snapshot, auditCursorAge);
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

    private static TimeSpan? CalculateAge(
        DateTimeOffset nowUtc,
        DateTimeOffset? atUtc) =>
        atUtc is DateTimeOffset value
            ? nowUtc > value ? nowUtc - value : TimeSpan.Zero
            : null;

    private static TimeSpan? CalculateRetentionHeadroom(
        DateTimeOffset? oldestPendingRequiredFromAtUtc,
        DateTimeOffset? retentionBoundaryAtUtc)
    {
        if (oldestPendingRequiredFromAtUtc is not DateTimeOffset required ||
            retentionBoundaryAtUtc is not DateTimeOffset boundary)
        {
            return null;
        }

        return required - boundary;
    }

    private static bool IsSourceCoverageMissing(
        DateTimeOffset? sourceOldestPersistedAtUtc,
        DateTimeOffset? oldestPendingRequiredFromAtUtc) =>
        sourceOldestPersistedAtUtc is DateTimeOffset sourceOldest &&
        oldestPendingRequiredFromAtUtc is DateTimeOffset required &&
        sourceOldest > required;

    private static bool IsAtOrAbove(TimeSpan? value, TimeSpan threshold) =>
        value is TimeSpan actual && actual >= threshold;

    private static bool IsAtOrBelow(TimeSpan? value, TimeSpan threshold) =>
        value is TimeSpan actual && actual <= threshold;

    private static StatisticsHealthEvaluation Evaluation(
        StatisticsHealthStatus status,
        string reason,
        TimeSpan? lag,
        TimeSpan? pendingAge,
        TimeSpan? retentionHeadroom,
        ProjectionOperationalSnapshot snapshot,
        TimeSpan? auditCursorAge) =>
        new(status, reason, lag, pendingAge, retentionHeadroom, snapshot.PendingRequestCount, snapshot.CoverageGapCount, auditCursorAge);
}
