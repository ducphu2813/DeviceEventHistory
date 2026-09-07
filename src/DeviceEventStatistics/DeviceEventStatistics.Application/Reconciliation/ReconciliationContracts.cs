using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Metadata;
using DeviceEventStatistics.Domain.State;

namespace DeviceEventStatistics.Application.Reconciliation;

public static class ReconciliationRequestStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public static class ReconciliationReasonCodes
{
    public const string Bootstrap = "STAT_BOOTSTRAP";
    public const string Backfill = "STAT_BACKFILL";
    public const string Rebuild = "STAT_REBUILD";
    public const string ForwardPropagation = "STAT_FORWARD_PROPAGATION";
    public const string SourceRetentionGap = "STAT_SOURCE_RETENTION_GAP";
    public const string SourceIdentityMissing = "STAT_RECONCILIATION_SOURCE_IDENTITY_MISSING";
    public const string OpeningStateEvidenceMissing = "STAT_OPENING_STATE_EVIDENCE_MISSING";
    public const string CoverageUnavailable = "STAT_RECONCILIATION_COVERAGE_UNAVAILABLE";
    public const string InvalidRequest = "STAT_RECONCILIATION_REQUEST_INVALID";
    public const string RollingSchedule = "STAT_ROLLING_SCHEDULE";
    public const string CurrentRangeOpen = "STAT_CURRENT_RANGE_OPEN";
}

public sealed record ReconciliationRequest(
    long RequestId,
    ProjectionIdentity Identity,
    StateStreamKey Key,
    DateOnly FromStatisticsDate,
    DateOnly ToStatisticsDate,
    string ReasonCode,
    string Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    string? ClaimOwner,
    long? ClaimEpoch,
    DateTimeOffset? ClaimExpiresAtUtc,
    long DirtyGeneration,
    DateTimeOffset RequestedAtUtc,
    string? ErrorSummary,
    string? EvidenceEventId = null);

public sealed record ReconciliationClaim(
    ReconciliationRequest Request,
    string Owner,
    long Epoch,
    DateTimeOffset ExpiresAtUtc);

public sealed record ReconciliationRequestSeed(
    ProjectionIdentity Identity,
    StateStreamKey Key,
    DateOnly FromStatisticsDate,
    DateOnly ToStatisticsDate,
    string ReasonCode,
    DateTimeOffset RequestedAtUtc,
    string EvidenceEventId);

public sealed record ReconciliationMembership(
    string EventId,
    string SourceDocumentId);

public sealed record ReconciliationSnapshot(
    Guid RunId,
    ReconciliationClaim Claim,
    DateTimeOffset FromTimelineAtUtc,
    DateTimeOffset ToTimelineAtUtc,
    long CapturedDataRevision,
    IReadOnlyCollection<ReconciliationMembership> Membership,
    IReadOnlyDictionary<StateStreamKey, StateCursorSnapshot> OpeningCursors);

public sealed record ReconciliationExecutionOptions(
    string MappingVersion,
    int MetricSetVersion,
    TimeSpan ClaimDuration,
    TimeSpan RetryDelay,
    int MaxAttempts,
    int PageSize,
    int MaximumRangeDays,
    int MaximumRequestsPerRun,
    TimeSpan HistoryRetention,
    TimeSpan MinimumHistoryHeadroom,
    DateOnly CurrentEdgeDate);

public sealed record ReconciliationSourceResult(
    IReadOnlyList<ProcessedEventInput> ProcessedEvents,
    IReadOnlyList<MetricContribution> MetricContributions,
    IReadOnlyList<DeviceSummaryContribution> DeviceSummaries,
    IReadOnlyList<StateDailyContribution> StateDailyContributions,
    IReadOnlyList<StateCursorInput> StateCursors,
    IReadOnlyList<QualityContribution> QualityContributions,
    IReadOnlyList<ProjectionCoverageInput> Coverage,
    int ReadEventCount,
    IReadOnlyList<DeviceMetadata> DeviceDimensions);

public sealed record ProjectionCoverageInput(
    long CompanyId,
    long DeviceId,
    DateOnly StatisticsDate,
    string CoverageKind,
    string CoverageStatus,
    DateTimeOffset CoveredFromAtUtc,
    DateTimeOffset CoveredThroughAtUtc,
    string? ReasonCode);

public sealed record ReconciliationPublishResult(
    Guid RunId,
    int AffectedRowCount,
    long DataRevision);

public interface IReconciliationRequestStore
{
    Task EnqueueAsync(
        IReadOnlyCollection<ReconciliationRequestSeed> requests,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default);

    Task<ReconciliationClaim?> ClaimNextAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        TimeSpan claimDuration,
        int maximumAttempts,
        CancellationToken cancellationToken = default);

    Task<ReconciliationClaim> LimitRangeAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        int maximumRangeDays,
        CancellationToken cancellationToken = default);

    Task<ReconciliationClaim> ExtendToCurrentEdgeAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        DateOnly currentEdgeDate,
        CancellationToken cancellationToken = default);

    Task<bool> RenewAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        string errorSummary,
        bool permanent,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);
}

public interface IProjectionRebuildStore
{
    Task<ReconciliationSnapshot> CaptureAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default);

    Task StageAsync(
        ReconciliationSnapshot snapshot,
        ReconciliationSourceResult result,
        CancellationToken cancellationToken = default);

    Task<ReconciliationPublishResult> PublishAsync(
        ReconciliationSnapshot snapshot,
        ReconciliationSourceResult result,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default);

    Task CleanupAsync(Guid runId, CancellationToken cancellationToken = default);
}

public sealed record ProjectionRecoveryRun(
    ProjectionIdentity Identity,
    Guid RunId,
    string RunType,
    DateOnly FromStatisticsDate,
    DateOnly ToStatisticsDate,
    long? CompanyId,
    long? DeviceId,
    DateTimeOffset StartedAtUtc);

public static class ProjectionRunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public interface IProjectionRecoveryStore
{
    Task StartRunAsync(
        ProjectionRecoveryRun run,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default);

    Task CompleteRunAsync(
        ProjectionRecoveryRun run,
        ProjectionLeaseToken lease,
        string status,
        long readEventCount,
        long affectedRowCount,
        string? errorSummary = null,
        CancellationToken cancellationToken = default);
}

public sealed record OperationalCleanupResult(
    int DeletedStagingRows,
    int DeletedProjectionRuns);

public interface IOperationalCleanupStore
{
    Task<OperationalCleanupResult> CleanupAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        DateTimeOffset projectionRunCutoffAtUtc,
        DateTimeOffset stagingCutoffAtUtc,
        CancellationToken cancellationToken = default);
}
